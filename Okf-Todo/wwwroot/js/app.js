(function ($) {
  const pendingRequests = new Map()
  const bridgeTimeoutMs = 15000
  const imageBridgeTimeoutMs = 120000
  const viewLabels = {
    active: 'Active',
    completed: 'Completed',
    all: 'All'
  }
  const supportedEditorImageTypes = ['image/png', 'image/jpeg', 'image/gif', 'image/webp']
  const maxEditorImageBytes = 5 * 1024 * 1024
  const wideLayoutMediaQuery = window.matchMedia('(min-width: 901px)')
  const layoutStorageKeys = {
    width: 'okfTodo.taskListWidth',
    height: 'okfTodo.taskListHeight'
  }

  let lookups = null
  let tasks = []
  let currentTask = null
  let currentView = 'active'
  let isEditorReady = false
  let isDirty = false
  let preferredBodyFormatCode = 'HTML'

  function createMessageId() {
    if (window.crypto && window.crypto.randomUUID) {
      return window.crypto.randomUUID()
    }

    return `${Date.now()}-${Math.random().toString(16).slice(2)}`
  }

  function sendNativeMessage(message) {
    const serializedMessage = JSON.stringify(message)

    if (window.external && typeof window.external.sendMessage === 'function') {
      window.external.sendMessage(serializedMessage)
      return
    }

    if (window.chrome && window.chrome.webview) {
      window.chrome.webview.postMessage(serializedMessage)
      return
    }

    throw new Error('Photino message bridge is unavailable.')
  }

  function sendBridgeMessage(type, payload) {
    const messageId = createMessageId()
    const timeoutMs = type === 'image.create' || type === 'image.get'
      ? imageBridgeTimeoutMs
      : bridgeTimeoutMs

    return new Promise(function (resolve, reject) {
      const timeoutId = window.setTimeout(function () {
        pendingRequests.delete(messageId)
        reject(new Error(`Timed out waiting for ${type}.`))
      }, timeoutMs)

      pendingRequests.set(messageId, {
        resolve: function (value) {
          window.clearTimeout(timeoutId)
          resolve(value)
        },
        reject: function (error) {
          window.clearTimeout(timeoutId)
          reject(error)
        }
      })

      try {
        sendNativeMessage({
          messageId,
          type,
          payload
        })
      } catch (error) {
        pendingRequests.delete(messageId)
        window.clearTimeout(timeoutId)
        reject(error)
      }
    })
  }

  function receiveBridgeMessage(message) {
    let response

    try {
      response = typeof message === 'string' ? JSON.parse(message) : message
    } catch {
      return
    }

    const pendingRequest = pendingRequests.get(response.messageId)
    if (!pendingRequest) {
      return
    }

    pendingRequests.delete(response.messageId)

    if (response.ok) {
      pendingRequest.resolve(response.payload)
      return
    }

    pendingRequest.reject(response.error || {
      code: 'UnexpectedError',
      message: 'Unexpected bridge error.'
    })
  }

  function initializeBridgeReceiver() {
    try {
      if (window.external && typeof window.external.receiveMessage === 'function') {
        window.external.receiveMessage(receiveBridgeMessage)
      }
    } catch (error) {
      setStatus(error.message || 'Could not register Photino bridge receiver', 'error')
    }

    try {
      if (window.chrome && window.chrome.webview) {
        window.chrome.webview.addEventListener('message', function (event) {
          receiveBridgeMessage(event.data)
        })
      }
    } catch (error) {
      setStatus(error.message || 'Could not register WebView bridge receiver', 'error')
    }
  }

  function encodeText(value) {
    return String(value || '')
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
  }

  function encodeAttribute(value) {
    return encodeText(value).replace(/"/g, '&quot;')
  }

  function normalizeBadgeColor(value) {
    const color = String(value || '').trim()
    return /^#[0-9a-fA-F]{6}$/.test(color) ? color : null
  }

  function renderBadge(label, backgroundColor, foregroundColor) {
    const safeBackground = normalizeBadgeColor(backgroundColor)
    const safeForeground = normalizeBadgeColor(foregroundColor)
    const style = safeBackground && safeForeground
      ? ` style="background-color: ${safeBackground}; color: ${safeForeground};"`
      : ''

    return `<span class="task-badge"${style}>${encodeText(label)}</span>`
  }

  function formatDate(value) {
    if (!value) {
      return ''
    }

    return String(value).slice(0, 10)
  }

  function formatShortDate(value) {
    const formatted = formatDate(value)
    return formatted || ''
  }

  function setStatus(message, state) {
    $('#save-status')
      .removeClass('is-ready is-dirty is-saved is-error')
      .addClass(state ? `is-${state}` : '')
      .text(message)
  }

  function getErrorMessage(error, fallback) {
    return error && error.message ? error.message : fallback
  }

  function getViewForTask(task) {
    const statusCode = task && task.taskStatusCode

    if (statusCode === 'COMPLETED' || statusCode === 'CANCELLED') {
      return 'completed'
    }

    return 'active'
  }

  function selectViewForTask(task) {
    currentView = getViewForTask(task)
  }

  function readBlobAsDataUrl(blob) {
    return new Promise(function (resolve, reject) {
      const reader = new FileReader()

      reader.onload = function () {
        resolve(reader.result)
      }

      reader.onerror = function () {
        reject(new Error('Could not read selected image.'))
      }

      reader.readAsDataURL(blob)
    })
  }

  async function readBlobAsBase64(blob) {
    const dataUrl = await readBlobAsDataUrl(blob)
    const markerIndex = String(dataUrl).indexOf(',')

    if (markerIndex < 0) {
      throw new Error('Could not read selected image.')
    }

    return String(dataUrl).slice(markerIndex + 1)
  }

  function chooseEditorImageFile() {
    return new Promise(function (resolve) {
      const input = document.createElement('input')
      input.type = 'file'
      input.accept = supportedEditorImageTypes.join(',')
      input.style.position = 'fixed'
      input.style.left = '-10000px'
      input.style.width = '1px'
      input.style.height = '1px'

      input.addEventListener('change', function () {
        const file = input.files && input.files.length > 0 ? input.files[0] : null
        input.remove()
        resolve(file)
      })

      document.body.appendChild(input)
      input.click()
    })
  }

  async function pickEditorImage(blob) {
    if (!currentTask || !currentTask.id) {
      setStatus('Save the task before adding images', 'error')
      return null
    }

    const imageBlob = blob || await chooseEditorImageFile()

    if (!imageBlob) {
      return null
    }

    if (!supportedEditorImageTypes.includes(imageBlob.type)) {
      setStatus('Select a PNG, JPEG, GIF, or WebP image', 'error')
      return null
    }

    if (imageBlob.size > maxEditorImageBytes) {
      setStatus('Image must be 5 MB or smaller', 'error')
      return null
    }

    const image = await sendBridgeMessage('image.create', {
      taskId: currentTask.id,
      issueId: null,
      filename: imageBlob.name || null,
      mimeType: imageBlob.type,
      base64Data: await readBlobAsBase64(imageBlob),
      width: null,
      height: null
    })

    return {
      src: image.src,
      attributes: {
        alt: imageBlob.name || 'image'
      }
    }
  }

  function getEditorDisplayBody(body) {
    return body || ''
  }

  function getPersistedEditorBody() {
    const modeCode = $('#editor-mode').val().toString()
    const body = modeCode === 'MARKDOWN'
      ? window.Editor.getMarkdown()
      : window.Editor.getHtml()

    if (modeCode === 'MARKDOWN' || !body) {
      return body
    }

    const template = document.createElement('template')
    template.innerHTML = body
    template.content.querySelectorAll('img').forEach(function (image) {
      const stableSrc = image.getAttribute('data-app-src')
      if (!stableSrc) {
        return
      }

      image.setAttribute('src', stableSrc)
      image.removeAttribute('data-app-src')
    })

    return template.innerHTML
  }

  function setFieldInvalid(selector, isInvalid) {
    $(selector).toggleClass('is-invalid', isInvalid)
  }

  function clearValidationState() {
    $('#task-title, #task-type, #waiting-text').removeClass('is-invalid')
  }

  function markDirty() {
    if (!isEditorReady) {
      return
    }

    clearValidationState()
    isDirty = true
    setStatus('Unsaved changes', 'dirty')
  }

  function renderShell() {
    $('#app').html(`
      <main class="app-shell">
        <aside class="task-sidebar" aria-labelledby="app-title">
          <header class="sidebar-header">
            <div>
              <p class="eyebrow">Local task system</p>
              <h1 id="app-title">OKF Tasks</h1>
            </div>
            <button id="new-task-button" type="button">New task</button>
          </header>

          <div class="view-tabs" aria-label="Task views">
            ${Object.keys(viewLabels).map(function (view) {
              return `<button class="view-tab" type="button" data-view="${view}">${viewLabels[view]}</button>`
            }).join('')}
          </div>

          <label class="sr-only" for="task-search">Search tasks</label>
          <input id="task-search" class="task-search" type="search" placeholder="Search tasks" autocomplete="off">

          <div id="task-list" class="task-list" aria-label="Tasks"></div>
        </aside>

        <div id="layout-resizer" class="layout-resizer" role="separator" aria-label="Resize task list" aria-orientation="vertical" tabindex="0"></div>

        <section class="task-editor-panel" aria-labelledby="task-editor-title">
          <header class="editor-header">
            <div>
              <p id="task-status-label" class="eyebrow">No task selected</p>
              <h2 id="task-editor-title">Select or create a task</h2>
            </div>
            <div class="app-actions" aria-label="Task actions">
              <button id="settings-button" class="icon-button" type="button" aria-label="Settings" title="Settings">&#9881;</button>
              <span id="save-status" class="save-status is-ready" role="status">Ready</span>
              <button id="complete-button" class="secondary-button" type="button" disabled>Complete</button>
              <button id="cancel-button" class="secondary-button danger-button" type="button" disabled>Cancel</button>
              <button id="save-button" type="button" disabled>Save</button>
            </div>
          </header>

          <form id="task-form" class="task-form">
            <label class="field-label" for="task-title">Title</label>
            <input id="task-title" class="title-input" type="text" autocomplete="off" required disabled>

            <div class="metadata-grid">
              <label class="field-block" for="task-type">
                <span>Task type</span>
                <select id="task-type" required disabled></select>
              </label>
              <label class="field-block" for="task-priority">
                <span>Priority</span>
                <select id="task-priority" disabled></select>
              </label>
              <label class="field-block" for="task-deadline">
                <span>Deadline</span>
                <input id="task-deadline" type="date" disabled>
              </label>
              <label class="field-block" for="task-source">
                <span>Source</span>
                <select id="task-source" disabled></select>
              </label>
              <label class="field-block" for="task-source-reference">
                <span>Source reference</span>
                <input id="task-source-reference" type="text" autocomplete="off" disabled>
              </label>
              <label class="field-block" for="task-source-url">
                <span>Source URL</span>
                <input id="task-source-url" type="url" autocomplete="off" disabled>
              </label>
            </div>

            <section class="waiting-panel" aria-labelledby="waiting-label">
              <label id="waiting-label" class="waiting-label" for="waiting-text">Waiting for</label>
              <input id="waiting-text" type="text" autocomplete="off" placeholder="INC123456" disabled>
              <button id="add-waiting-button" class="secondary-button" type="button" disabled>Set</button>
              <button id="clear-waiting-button" class="secondary-button danger-button" type="button" disabled>Clear</button>
            </section>

            <div class="body-header">
              <label class="field-label" for="text-body">Body</label>
            </div>
            <div id="editor-host" class="editor-host">
              <textarea id="text-body"></textarea>
            </div>
          </form>
        </section>

        <div id="settings-overlay" class="modal-overlay" hidden>
          <section class="settings-dialog" role="dialog" aria-modal="true" aria-labelledby="settings-title">
            <header class="settings-header">
              <h2 id="settings-title">Settings</h2>
              <button id="settings-close-button" class="icon-button" type="button" aria-label="Close settings" title="Close">&times;</button>
            </header>

            <label class="settings-field" for="editor-mode">
              <span>Editor mode</span>
              <select id="editor-mode" disabled>
                <option value="HTML">HTML</option>
                <option value="MARKDOWN">Markdown</option>
              </select>
            </label>

            <p class="settings-help">If you do not understand this option, choose HTML.</p>
          </section>
        </div>

        <div id="new-task-overlay" class="modal-overlay" hidden>
          <section class="settings-dialog new-task-dialog" role="dialog" aria-modal="true" aria-labelledby="new-task-dialog-title">
            <header class="settings-header">
              <h2 id="new-task-dialog-title">New task</h2>
            </header>

            <label class="settings-field" for="new-task-title-input">
              <span>Title</span>
              <input id="new-task-title-input" type="text" autocomplete="off" required>
            </label>

            <p id="new-task-error" class="form-error" hidden>Title is required.</p>

            <div class="modal-actions">
              <button id="new-task-cancel-button" class="secondary-button" type="button">Cancel</button>
              <button id="new-task-save-button" type="button">Save</button>
            </div>
          </section>
        </div>
      </main>
    `)
  }

  function renderLookupOptions(selector, items, includeEmpty) {
    const options = []

    if (includeEmpty) {
      options.push('<option value="">None</option>')
    }

    ;(items || []).forEach(function (item) {
      options.push(`<option value="${encodeAttribute(item.code)}">${encodeText(item.name)}</option>`)
    })

    $(selector).html(options.join(''))
  }

  function renderLookups() {
    renderLookupOptions('#task-type', lookups.taskTypes, false)
    renderLookupOptions('#task-priority', lookups.taskPriorities, true)
    renderLookupOptions('#task-source', lookups.taskSources, true)
    renderLookupOptions('#editor-mode', lookups.bodyFormats, false)
  }

  function getSupportedBodyFormatCode(code) {
    const normalizedCode = (code || 'HTML').toString().trim().toUpperCase()
    const bodyFormats = lookups && lookups.bodyFormats ? lookups.bodyFormats : []

    if (bodyFormats.some(function (format) { return format.code === normalizedCode })) {
      return normalizedCode
    }

    const htmlFormat = bodyFormats.find(function (format) {
      return format.code === 'HTML'
    })

    return htmlFormat ? htmlFormat.code : (bodyFormats[0] ? bodyFormats[0].code : 'HTML')
  }

  async function loadEditorPreference() {
    const preference = await sendBridgeMessage('editor.preference.get', {})
    preferredBodyFormatCode = getSupportedBodyFormatCode(preference.bodyFormatCode)
    $('#editor-mode').val(preferredBodyFormatCode)
  }

  function saveEditorPreference(bodyFormatCode) {
    preferredBodyFormatCode = getSupportedBodyFormatCode(bodyFormatCode)

    return sendBridgeMessage('editor.preference.save', {
      bodyFormatCode: preferredBodyFormatCode
    })
  }

  function renderEmptyEditor() {
    currentTask = null
    isDirty = false
    isEditorReady = false
    clearValidationState()

    if (window.Editor && typeof window.Editor.destroy === 'function') {
      window.Editor.destroy()
    }

    $('#task-status-label').text('No task selected')
    $('#task-editor-title').text('Select or create a task')
    $('#task-title').val('')
    $('#task-type').val('')
    $('#task-priority').val('')
    $('#task-deadline').val('')
    $('#task-source').val('')
    $('#task-source-reference').val('')
    $('#task-source-url').val('')
    $('#editor-mode').val(getSupportedBodyFormatCode(preferredBodyFormatCode))
    $('#waiting-text').val('')
    $('#task-form input, #task-form select').prop('disabled', true)
    $('#add-waiting-button, #clear-waiting-button, #complete-button, #cancel-button, #save-button').prop('disabled', true)
    $('#editor-mode').prop('disabled', true)
    $('#editor-host').html('<div class="empty-editor">Select a task to edit the body.</div>')
    setStatus('Ready', 'ready')
    renderTaskList()
  }

  function waitForNextPaint() {
    return new Promise(function (resolve) {
      window.requestAnimationFrame(function () {
        window.requestAnimationFrame(resolve)
      })
    })
  }

  function getStoredSplitValue(key, fallback) {
    let storedValue = null

    try {
      storedValue = window.localStorage.getItem(key)
    } catch {
      storedValue = null
    }

    const value = Number(storedValue)
    return Number.isFinite(value) && value > 0 ? value : fallback
  }

  function storeSplitValue(key, value) {
    try {
      window.localStorage.setItem(key, String(Math.round(value)))
    } catch {
      // The splitter still works for the current session when storage is unavailable.
    }
  }

  function clampSplitValue(value, minimum, maximum) {
    return Math.max(minimum, Math.min(maximum, value))
  }

  function getLayoutBounds() {
    const shell = $('.app-shell')[0]
    const rect = shell.getBoundingClientRect()

    return {
      width: rect.width || window.innerWidth,
      height: rect.height || window.innerHeight
    }
  }

  function setTaskListWidth(width) {
    const bounds = getLayoutBounds()
    const clampedWidth = clampSplitValue(width, 220, Math.max(260, bounds.width - 460))
    document.documentElement.style.setProperty('--task-list-width', `${clampedWidth}px`)
    storeSplitValue(layoutStorageKeys.width, clampedWidth)
  }

  function setTaskListHeight(height) {
    const bounds = getLayoutBounds()
    const clampedHeight = clampSplitValue(height, 170, Math.max(220, bounds.height - 380))
    document.documentElement.style.setProperty('--task-list-height', `${clampedHeight}px`)
    storeSplitValue(layoutStorageKeys.height, clampedHeight)
  }

  function isWideLayout() {
    return wideLayoutMediaQuery.matches
  }

  function applyStoredLayoutSplit() {
    setTaskListWidth(getStoredSplitValue(layoutStorageKeys.width, 320))
    setTaskListHeight(getStoredSplitValue(layoutStorageKeys.height, Math.round(window.innerHeight * 0.42)))
    $('#layout-resizer').attr('aria-orientation', isWideLayout() ? 'vertical' : 'horizontal')
  }

  function bindLayoutResizer() {
    const $resizer = $('#layout-resizer')

    applyStoredLayoutSplit()

    const onMediaChange = function () {
      applyStoredLayoutSplit()
    }

    if (typeof wideLayoutMediaQuery.addEventListener === 'function') {
      wideLayoutMediaQuery.addEventListener('change', onMediaChange)
    } else if (typeof wideLayoutMediaQuery.addListener === 'function') {
      wideLayoutMediaQuery.addListener(onMediaChange)
    }

    $resizer.on('pointerdown', function (event) {
      event.preventDefault()
      const shell = $('.app-shell')[0]
      const shellRect = shell.getBoundingClientRect()
      $resizer.addClass('is-dragging')

      $(document).on('pointermove.layoutResizer', function (moveEvent) {
        if (isWideLayout()) {
          setTaskListWidth(moveEvent.clientX - shellRect.left)
        } else {
          setTaskListHeight(moveEvent.clientY - shellRect.top)
        }
      })

      $(document).on('pointerup.layoutResizer pointercancel.layoutResizer', function () {
        $resizer.removeClass('is-dragging')
        $(document).off('.layoutResizer')
      })
    })

    $resizer.on('keydown', function (event) {
      const step = event.shiftKey ? 40 : 20
      const currentWidth = getStoredSplitValue(layoutStorageKeys.width, 320)
      const currentHeight = getStoredSplitValue(layoutStorageKeys.height, Math.round(window.innerHeight * 0.42))

      if (isWideLayout() && (event.key === 'ArrowLeft' || event.key === 'ArrowRight')) {
        event.preventDefault()
        setTaskListWidth(currentWidth + (event.key === 'ArrowRight' ? step : -step))
      }

      if (!isWideLayout() && (event.key === 'ArrowUp' || event.key === 'ArrowDown')) {
        event.preventDefault()
        setTaskListHeight(currentHeight + (event.key === 'ArrowDown' ? step : -step))
      }
    })
  }

  function scheduleEditorPreload() {
    if (!window.Editor || typeof window.Editor.preloadHtml !== 'function') {
      return
    }

    const preload = function () {
      window.Editor.preloadHtml().catch(function () {
        // The visible editor initialization path reports load errors when needed.
      })
    }

    if (typeof window.requestIdleCallback === 'function') {
      window.requestIdleCallback(preload, { timeout: 3000 })
      return
    }

    window.setTimeout(preload, 1500)
  }

  function renderTaskList() {
    const query = $('#task-search').val().toString().trim().toLowerCase()
    const visibleTasks = query
      ? tasks.filter(function (task) {
        return task.title.toLowerCase().includes(query)
          || task.taskTypeName.toLowerCase().includes(query)
          || task.taskStatusName.toLowerCase().includes(query)
          || (task.taskPriorityName || '').toLowerCase().includes(query)
          || (task.activeWaitingForLabel || '').toLowerCase().includes(query)
      })
      : tasks

    $('.view-tab').removeClass('is-active')
    $(`.view-tab[data-view="${currentView}"]`).addClass('is-active')

    if (visibleTasks.length === 0) {
      const hasSearch = query.length > 0
      const title = hasSearch ? 'No matching tasks' : `No ${viewLabels[currentView].toLowerCase()} tasks`
      const detail = hasSearch ? 'Adjust the search text' : 'Create a task or switch view'

      $('#task-list').html(`
        <div class="empty-list">
          <strong>${encodeText(title)}</strong>
          <span>${encodeText(detail)}</span>
        </div>
      `)
      return
    }

    $('#task-list').html(visibleTasks.map(function (task) {
      const selectedClass = currentTask && currentTask.id === task.id ? ' is-selected' : ''
      const waitingClass = task.activeWaitingForLabel ? ' is-waiting' : ''
      const priority = task.taskPriorityName
        ? renderBadge(task.taskPriorityName, task.taskPriorityBackgroundColor, task.taskPriorityForegroundColor)
        : ''
      const deadline = task.deadline
        ? `<span class="task-badge">Due ${encodeText(formatShortDate(task.deadline))}</span>`
        : ''
      const waiting = task.activeWaitingForLabel
        ? `<span class="task-badge task-badge-waiting">Waiting: ${encodeText(task.activeWaitingForLabel)}</span>`
        : ''

      return `
        <button class="task-row${selectedClass}${waitingClass}" type="button" data-task-id="${task.id}">
          <span class="task-row-title">${encodeText(task.title)}</span>
          <span class="task-row-meta">
            ${renderBadge(task.taskTypeName, task.taskTypeBackgroundColor, task.taskTypeForegroundColor)}
            ${renderBadge(task.taskStatusName, task.taskStatusBackgroundColor, task.taskStatusForegroundColor)}
            ${priority}
            ${deadline}
            ${waiting}
          </span>
        </button>
      `
    }).join(''))
  }

  function createNewTaskPayload(title) {
    const firstTaskType = lookups.taskTypes && lookups.taskTypes[0]
    const bodyFormatCode = getSupportedBodyFormatCode(preferredBodyFormatCode)

    return {
      id: null,
      title,
      body: '',
      bodyFormatCode,
      taskTypeCode: firstTaskType ? firstTaskType.code : '',
      taskPriorityCode: '',
      taskSourceCode: '',
      sourceReference: '',
      sourceUrl: '',
      deadline: null,
      activeWaitingFor: null
    }
  }

  async function initializeEditor(modeCode, initialContent, initialHtml) {
    if (!window.Editor) {
      throw new Error('Editor service did not load.')
    }

    isEditorReady = false
    $('#save-button').prop('disabled', true)

    const editorMode = modeCode === 'MARKDOWN' ? 'markdown' : 'html'

    await window.Editor.initialize({
      mode: editorMode,
      selector: '#text-body',
      hostSelector: '#editor-host',
      baseUrl: '/tinymce',
      minHeight: 360,
      initialContent: initialContent || '',
      initialHtml: initialHtml || '',
      contentStyle:
        'body { color: #202124; font-family: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif; font-size: 15px; line-height: 1.55; }',
      onPickImage: pickEditorImage
    })

    window.Editor.markClean()
    isEditorReady = true
    $('#save-button').prop('disabled', false)
  }

  async function initializeEditorForTask(task) {
    const modeCode = getSupportedBodyFormatCode(preferredBodyFormatCode)
    const storedModeCode = task.bodyFormatCode === 'MARKDOWN' ? 'MARKDOWN' : 'HTML'
    const body = getEditorDisplayBody(task.body)

    if (modeCode === 'MARKDOWN' && storedModeCode === 'HTML') {
      await initializeEditor(modeCode, '', body)
      return
    }

    await initializeEditor(modeCode, body, null)
  }

  function describeWaiting(waitingFor) {
    if (!waitingFor) {
      return ''
    }

    return waitingFor.label || ''
  }

  function renderWaitingPanel(task) {
    const waitingFor = task.activeWaitingFor
    const canEditWaiting = !!(task.id && task.taskStatusCode === 'ACTIVE')

    $('#waiting-text').val(waitingFor ? describeWaiting(waitingFor) : '')

    $('#clear-waiting-button').prop('disabled', !waitingFor)
    $('#add-waiting-button').prop('disabled', !canEditWaiting)
    $('#waiting-text').prop('disabled', !canEditWaiting)
  }

  function renderTaskHeaderAndActions(task) {
    const isSavedTask = !!task.id
    const isFinal = task.taskStatusCode === 'COMPLETED' || task.taskStatusCode === 'CANCELLED'
    const canReopen = isSavedTask && isFinal
    const canCompleteOrCancel = isSavedTask && !isFinal

    $('#task-editor-title').text(task.id ? task.title : 'New task')
    $('#task-status-label').text(task.taskStatusName || 'Draft')
    $('#complete-button')
      .text(canReopen ? 'Reopen' : 'Complete')
      .prop('disabled', !(canCompleteOrCancel || canReopen))
    $('#cancel-button').prop('disabled', !canCompleteOrCancel)
    renderWaitingPanel(task)
  }

  function refreshCurrentTaskWithoutEditor(task) {
    currentTask = task
    $('#task-title').val(task.title || '')
    $('#task-type').val(task.taskTypeCode || '')
    $('#task-priority').val(task.taskPriorityCode || '')
    $('#task-deadline').val(formatDate(task.deadline))
    $('#task-source').val(task.taskSourceCode || '')
    $('#task-source-reference').val(task.sourceReference || '')
    $('#task-source-url').val(task.sourceUrl || '')
    $('#editor-mode').val(getSupportedBodyFormatCode(preferredBodyFormatCode))
    $('#task-form input, #task-form select').prop('disabled', false)
    $('#editor-mode').prop('disabled', false)
    renderTaskHeaderAndActions(task)
    renderTaskList()
  }

  async function renderTaskEditor(task) {
    currentTask = task
    isDirty = false
    clearValidationState()

    $('#task-title').val(task.title || '')
    $('#task-type').val(task.taskTypeCode || '')
    $('#task-priority').val(task.taskPriorityCode || '')
    $('#task-deadline').val(formatDate(task.deadline))
    $('#task-source').val(task.taskSourceCode || '')
    $('#task-source-reference').val(task.sourceReference || '')
    $('#task-source-url').val(task.sourceUrl || '')
    $('#editor-mode').val(getSupportedBodyFormatCode(preferredBodyFormatCode))
    renderWaitingPanel(task)

    $('#task-form input, #task-form select').prop('disabled', false)
    $('#editor-mode').prop('disabled', false)
    renderTaskHeaderAndActions(task)

    await initializeEditorForTask(task)
    setStatus(task.id ? 'Loaded' : 'Draft', 'ready')
    renderTaskList()
  }

  function getEditorBody() {
    return getPersistedEditorBody()
  }

  function getTaskPayload() {
    return {
      id: currentTask && currentTask.id ? currentTask.id : null,
      title: $('#task-title').val().toString().trim(),
      taskTypeCode: $('#task-type').val().toString(),
      body: getEditorBody(),
      bodyFormatCode: $('#editor-mode').val().toString(),
      taskPriorityCode: $('#task-priority').val().toString() || null,
      taskSourceCode: $('#task-source').val().toString() || null,
      sourceReference: $('#task-source-reference').val().toString().trim() || null,
      sourceUrl: $('#task-source-url').val().toString().trim() || null,
      deadline: $('#task-deadline').val().toString() || null
    }
  }

  async function switchEditorMode() {
    if (!currentTask || !isEditorReady) {
      return
    }

    const targetModeCode = $('#editor-mode').val().toString() === 'MARKDOWN' ? 'MARKDOWN' : 'HTML'
    const activeModeCode = window.Editor.getMode() === 'markdown' ? 'MARKDOWN' : 'HTML'

    if (targetModeCode === activeModeCode) {
      return
    }

    isEditorReady = false
    $('#save-button, #editor-mode').prop('disabled', true)
    setStatus('Switching editor', 'ready')

    try {
      if (targetModeCode === 'MARKDOWN') {
        await initializeEditor('MARKDOWN', '', window.Editor.getHtml())
      } else {
        await initializeEditor('HTML', window.Editor.getHtml(), null)
      }

      currentTask.bodyFormatCode = targetModeCode
      preferredBodyFormatCode = targetModeCode
      $('#editor-mode').val(targetModeCode)
      $('#editor-mode').prop('disabled', false)
      markDirty()
      await saveEditorPreference(targetModeCode).catch(function (error) {
        setStatus(getErrorMessage(error, 'Could not save editor preference'), 'error')
      })
    } catch (error) {
      $('#editor-mode').val(activeModeCode).prop('disabled', false)
      isEditorReady = true
      setStatus(getErrorMessage(error, 'Could not switch editor'), 'error')
    }
  }

  async function loadTasks(options) {
    const keepSelection = options && options.keepSelection
    tasks = await sendBridgeMessage('task.list', {
      view: currentView
    })
    renderTaskList()

    if (keepSelection && currentTask && currentTask.id) {
      const stillVisible = tasks.some(function (task) {
        return task.id === currentTask.id
      })
      if (stillVisible) {
        return
      }
    }

    renderTaskList()
  }

  async function selectTask(id) {
    const task = await sendBridgeMessage('task.get', {
      id
    })
    await renderTaskEditor(task)
  }

  async function saveTask() {
    if (!currentTask) {
      return
    }

    const isNewTask = !currentTask.id
    clearValidationState()
    const payload = getTaskPayload()
    if (!payload.title) {
      setFieldInvalid('#task-title', true)
      setStatus('Title is required', 'error')
      $('#task-title').trigger('focus')
      return
    }

    if (!payload.taskTypeCode) {
      setFieldInvalid('#task-type', true)
      setStatus('Task type is required', 'error')
      $('#task-type').trigger('focus')
      return
    }

    setStatus('Saving', 'ready')
    $('#save-button').prop('disabled', true)

    try {
      const savedTask = await sendBridgeMessage(currentTask.id ? 'task.update' : 'task.create', payload)
      if (currentTask.id) {
        refreshCurrentTaskWithoutEditor(savedTask)
      } else {
        await renderTaskEditor(savedTask)
      }

      if (isNewTask) {
        selectViewForTask(savedTask)
      }

      await loadTasks({ keepSelection: true })
      isDirty = false
      window.Editor.markClean()
      $('#save-button').prop('disabled', false)
      setStatus('Saved', 'saved')
    } catch (error) {
      $('#save-button').prop('disabled', false)
      throw error
    }
  }

  async function runLifecycleAction(type) {
    if (!currentTask || !currentTask.id) {
      return
    }

    if (!confirmActiveWaitClearBeforeLeavingActive(type)) {
      setStatus('Task remains active', 'ready')
      return
    }

    const task = await sendBridgeMessage(type, {
      id: currentTask.id
    })
    refreshCurrentTaskWithoutEditor(task)
    selectViewForTask(task)
    await loadTasks({ keepSelection: true })
    setStatus('Updated', 'saved')
  }

  function confirmActiveWaitClearBeforeLeavingActive(type) {
    const leavesActive = type === 'task.complete' || type === 'task.cancel'
    if (!leavesActive || !currentTask || currentTask.taskStatusCode !== 'ACTIVE' || !currentTask.activeWaitingFor) {
      return true
    }

    return window.confirm('This task has a waiting target. Clear waiting and continue?')
  }

  function runCompleteButtonAction() {
    const type = currentTask && (currentTask.taskStatusCode === 'COMPLETED' || currentTask.taskStatusCode === 'CANCELLED')
      ? 'task.reopen'
      : 'task.complete'

    return runLifecycleAction(type)
  }

  async function addWaitingFor() {
    if (!currentTask || !currentTask.id) {
      setStatus('Save the task before adding waiting', 'error')
      return
    }

    clearValidationState()
    const label = $('#waiting-text').val().toString().trim()

    if (!label) {
      setFieldInvalid('#waiting-text', true)
      setStatus('Waiting for is required', 'error')
      $('#waiting-text').trigger('focus')
      return
    }

    const task = await sendBridgeMessage('task.waiting.add', {
      taskId: currentTask.id,
      label
    })

    refreshCurrentTaskWithoutEditor(task)
    selectViewForTask(task)
    await loadTasks({ keepSelection: true })
    setStatus('Waiting target added', 'saved')
  }

  async function clearWaitingFor() {
    if (!currentTask || !currentTask.id || !currentTask.activeWaitingFor) {
      return
    }

    const task = await sendBridgeMessage('task.waiting.clear', {
      id: currentTask.id
    })

    refreshCurrentTaskWithoutEditor(task)
    selectViewForTask(task)
    await loadTasks({ keepSelection: true })
    setStatus('Waiting target cleared', 'saved')
  }

  function openSettings() {
    $('#settings-overlay').prop('hidden', false)
    $('#settings-close-button').trigger('focus')
  }

  function closeSettings() {
    $('#settings-overlay').prop('hidden', true)
    $('#settings-button').trigger('focus')
  }

  function openNewTaskDialog() {
    $('#new-task-title-input').val('').removeClass('is-invalid')
    $('#new-task-error').prop('hidden', true)
    $('#new-task-save-button, #new-task-cancel-button').prop('disabled', false)
    $('#new-task-overlay').prop('hidden', false)
    $('#new-task-title-input').trigger('focus')
  }

  function closeNewTaskDialog() {
    $('#new-task-overlay').prop('hidden', true)
    $('#new-task-button').trigger('focus')
  }

  async function createTaskFromDialog() {
    if ($('#new-task-save-button').prop('disabled')) {
      return
    }

    const title = $('#new-task-title-input').val().toString().trim()
    if (!title) {
      $('#new-task-title-input').addClass('is-invalid').trigger('focus')
      $('#new-task-error').prop('hidden', false)
      return
    }

    const payload = createNewTaskPayload(title)
    if (!payload.taskTypeCode) {
      setStatus('Task type lookup is missing', 'error')
      return
    }

    $('#new-task-save-button, #new-task-cancel-button').prop('disabled', true)
    setStatus('Saving', 'ready')

    try {
      const savedTask = await sendBridgeMessage('task.create', payload)
      closeNewTaskDialog()
      selectViewForTask(savedTask)
      await renderTaskEditor(savedTask)
      await loadTasks({ keepSelection: true })
      isDirty = false
      window.Editor.markClean()
      setStatus('Saved', 'saved')
    } catch (error) {
      $('#new-task-save-button, #new-task-cancel-button').prop('disabled', false)
      throw error
    }
  }

  function bindEvents() {
    $('#new-task-button').on('click', function () {
      openNewTaskDialog()
    })

    $('#settings-button').on('click', openSettings)
    $('#settings-close-button').on('click', closeSettings)
    $('#settings-overlay').on('click', function (event) {
      if (event.target === this) {
        closeSettings()
      }
    })
    $('#new-task-cancel-button').on('click', closeNewTaskDialog)
    $('#new-task-save-button').on('click', function () {
      createTaskFromDialog().catch(function (error) {
        setStatus(getErrorMessage(error, 'Could not create task'), 'error')
      })
    })
    $('#new-task-title-input').on('input', function () {
      $(this).removeClass('is-invalid')
      $('#new-task-error').prop('hidden', true)
    })
    $('#new-task-title-input').on('keydown', function (event) {
      if (event.key === 'Enter') {
        event.preventDefault()
        createTaskFromDialog().catch(function (error) {
          setStatus(getErrorMessage(error, 'Could not create task'), 'error')
        })
      }
    })
    $(document).on('keydown', function (event) {
      if (event.key === 'Escape' && !$('#settings-overlay').prop('hidden')) {
        closeSettings()
      }
      if (event.key === 'Escape' && !$('#new-task-overlay').prop('hidden')) {
        closeNewTaskDialog()
      }
    })

    $('#task-list').on('click', '.task-row', function () {
      const taskId = Number($(this).attr('data-task-id'))
      selectTask(taskId).catch(function (error) {
        setStatus(getErrorMessage(error, 'Could not load task'), 'error')
      })
    })

    $('.view-tab').on('click', function () {
      currentView = $(this).attr('data-view')
      renderEmptyEditor()
      loadTasks().catch(showFatalError)
    })

    $('#task-search').on('input', renderTaskList)
    $('#task-form').on('input change', '#task-title, #task-type, #task-priority, #task-deadline, #task-source, #task-source-reference, #task-source-url', markDirty)
    $('#editor-mode').on('change', function () {
      switchEditorMode().catch(function (error) {
        setStatus(getErrorMessage(error, 'Could not switch editor'), 'error')
      })
    })
    $('#save-button').on('click', function () {
      saveTask().catch(function (error) {
        setStatus(getErrorMessage(error, 'Could not save task'), 'error')
      })
    })
    $('#complete-button').on('click', function () {
      runCompleteButtonAction().catch(function (error) {
        setStatus(getErrorMessage(error, 'Could not update completion state'), 'error')
      })
    })
    $('#cancel-button').on('click', function () {
      runLifecycleAction('task.cancel').catch(function (error) {
        setStatus(getErrorMessage(error, 'Could not cancel task'), 'error')
      })
    })
    $('#add-waiting-button').on('click', function () {
      addWaitingFor().catch(function (error) {
        setStatus(getErrorMessage(error, 'Could not add waiting target'), 'error')
      })
    })
    $('#clear-waiting-button').on('click', function () {
      clearWaitingFor().catch(function (error) {
        setStatus(getErrorMessage(error, 'Could not clear waiting target'), 'error')
      })
    })

    window.Editor.onChanged(markDirty)
  }

  function showFatalError(error) {
    const message = typeof error === 'string' ? error : (error.message || 'The task workspace could not be loaded.')
    $('#app').html(`
      <main class="app-shell">
        <section class="task-editor-panel">
          <p class="empty-state">${encodeText(message)}</p>
        </section>
      </main>
    `)
  }

  $(async function () {
    renderShell()
    initializeBridgeReceiver()
    bindLayoutResizer()
    bindEvents()
    renderEmptyEditor()

    try {
      await waitForNextPaint()
      lookups = await sendBridgeMessage('task.lookups.get', {})
      renderLookups()
      await loadEditorPreference()
      await loadTasks()
      scheduleEditorPreload()
    } catch (error) {
      showFatalError(error)
    }
  })
})(jQuery)
