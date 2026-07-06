(function ($) {
  const pendingRequests = new Map()
  const bridgeTimeoutMs = 15000
  const imageBridgeTimeoutMs = 120000
  const viewLabels = {
    inbox: 'Inbox',
    active: 'Active',
    completed: 'Completed',
    all: 'All'
  }
  const supportedEditorImageTypes = ['image/png', 'image/jpeg', 'image/gif', 'image/webp']
  const maxEditorImageBytes = 5 * 1024 * 1024

  let lookups = null
  let tasks = []
  let currentTask = null
  let currentView = 'active'
  let isEditorReady = false
  let isDirty = false

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

        <section class="task-editor-panel" aria-labelledby="task-editor-title">
          <header class="editor-header">
            <div>
              <p id="task-status-label" class="eyebrow">No task selected</p>
              <h2 id="task-editor-title">Select or create a task</h2>
            </div>
            <div class="app-actions" aria-label="Task actions">
              <span id="save-status" class="save-status is-ready" role="status">Ready</span>
              <button id="start-button" class="secondary-button" type="button" disabled>Start</button>
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
              <label class="mode-field" for="editor-mode">
                <span>Editor</span>
                <select id="editor-mode" disabled>
                  <option value="HTML">HTML</option>
                  <option value="MARKDOWN">Markdown</option>
                </select>
              </label>
            </div>
            <div id="editor-host" class="editor-host">
              <textarea id="text-body"></textarea>
            </div>
          </form>
        </section>
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

  function renderTaskList() {
    const query = $('#task-search').val().toString().trim().toLowerCase()
    const visibleTasks = query
      ? tasks.filter(function (task) {
        return task.title.toLowerCase().includes(query)
          || task.taskTypeName.toLowerCase().includes(query)
          || task.taskStatusName.toLowerCase().includes(query)
          || (task.taskPriorityName || '').toLowerCase().includes(query)
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
      const priority = task.taskPriorityName
        ? `<span>${encodeText(task.taskPriorityName)}</span>`
        : ''
      const deadline = task.deadline
        ? `<span>Due ${encodeText(formatShortDate(task.deadline))}</span>`
        : ''

      return `
        <button class="task-row${selectedClass}" type="button" data-task-id="${task.id}">
          <span class="task-row-title">${encodeText(task.title)}</span>
          <span class="task-row-meta">
            <span>${encodeText(task.taskTypeName)}</span>
            <span>${encodeText(task.taskStatusName)}</span>
            ${priority}
            ${deadline}
          </span>
        </button>
      `
    }).join(''))
  }

  function createDraftTask() {
    const firstTaskType = lookups.taskTypes && lookups.taskTypes[0]
    const htmlFormat = (lookups.bodyFormats || []).find(function (format) {
      return format.code === 'HTML'
    }) || (lookups.bodyFormats || [])[0]

    return {
      id: null,
      title: '',
      body: '',
      bodyFormatCode: htmlFormat ? htmlFormat.code : 'HTML',
      taskTypeCode: firstTaskType ? firstTaskType.code : '',
      taskStatusCode: 'DRAFT',
      taskStatusName: 'Draft',
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
    const modeCode = task.bodyFormatCode === 'MARKDOWN' ? 'MARKDOWN' : 'HTML'
    await initializeEditor(modeCode, getEditorDisplayBody(task.body), null)
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
    $('#add-waiting-button').prop('disabled', !canEditWaiting || !!waitingFor)
    $('#waiting-text').prop('disabled', !canEditWaiting || !!waitingFor)
  }

  function renderTaskHeaderAndActions(task) {
    const isSavedTask = !!task.id
    const isFinal = task.taskStatusCode === 'COMPLETED' || task.taskStatusCode === 'CANCELLED'
    const canStartOrUndoStart = isSavedTask && (task.taskStatusCode === 'NEW' || task.taskStatusCode === 'ACTIVE')
    const canReopen = isSavedTask && isFinal
    const canCompleteOrCancel = isSavedTask && !isFinal

    $('#task-editor-title').text(task.id ? task.title : 'New task')
    $('#task-status-label').text(task.taskStatusName || 'Draft')
    $('#start-button')
      .text(task.taskStatusCode === 'ACTIVE' ? 'Undo start' : 'Start')
      .prop('disabled', !canStartOrUndoStart)
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
    $('#editor-mode').val(task.bodyFormatCode || 'HTML')
    $('#task-form input, #task-form select').prop('disabled', false)
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
    $('#editor-mode').val(task.bodyFormatCode || 'HTML')
    renderWaitingPanel(task)

    $('#task-form input, #task-form select').prop('disabled', false)
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
      $('#editor-mode').val(targetModeCode)
      $('#editor-mode').prop('disabled', false)
      markDirty()
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

    if (!currentTask && tasks.length > 0) {
      await selectTask(tasks[0].id)
    }
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
    await loadTasks({ keepSelection: true })
    setStatus('Updated', 'saved')
  }

  function confirmActiveWaitClearBeforeLeavingActive(type) {
    const leavesActive = type === 'task.undoStart' || type === 'task.complete' || type === 'task.cancel'
    if (!leavesActive || !currentTask || currentTask.taskStatusCode !== 'ACTIVE' || !currentTask.activeWaitingFor) {
      return true
    }

    return window.confirm('This task has a waiting target. Clear waiting and continue?')
  }

  function runStartButtonAction() {
    const type = currentTask && currentTask.taskStatusCode === 'ACTIVE'
      ? 'task.undoStart'
      : 'task.start'

    return runLifecycleAction(type)
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
    await loadTasks({ keepSelection: true })
    setStatus('Waiting target cleared', 'saved')
  }

  function bindEvents() {
    $('#new-task-button').on('click', function () {
      renderTaskEditor(createDraftTask()).catch(showFatalError)
    })

    $('#task-list').on('click', '.task-row', function () {
      const taskId = Number($(this).attr('data-task-id'))
      selectTask(taskId).catch(function (error) {
        setStatus(getErrorMessage(error, 'Could not load task'), 'error')
      })
    })

    $('.view-tab').on('click', function () {
      currentView = $(this).attr('data-view')
      currentTask = null
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
    $('#start-button').on('click', function () {
      runStartButtonAction().catch(function (error) {
        setStatus(getErrorMessage(error, 'Could not update start state'), 'error')
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
    bindEvents()

    try {
      lookups = await sendBridgeMessage('task.lookups.get', {})
      renderLookups()
      await loadTasks()

      if (tasks.length === 0) {
        await renderTaskEditor(createDraftTask())
      }
    } catch (error) {
      showFatalError(error)
    }
  })
})(jQuery)
