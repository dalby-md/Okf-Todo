(function ($) {
  const pendingRequests = new Map()
  const bridgeTimeoutMs = 15000
  const imageBridgeTimeoutMs = 120000
  const viewLabels = {
    active: 'Active',
    completed: 'Completed',
    all: 'All'
  }
  const lookupSettingsGroups = {
    taskTypes: 'Task types',
    taskPriorities: 'Priorities',
    taskStatuses: 'Statuses'
  }
  const lookupSettingsGroupNouns = {
    taskTypes: 'task type',
    taskPriorities: 'priority',
    taskStatuses: 'status'
  }
  const supportedEditorImageTypes = ['image/png', 'image/jpeg', 'image/gif', 'image/webp']
  const maxEditorImageBytes = 5 * 1024 * 1024
  const wideLayoutMediaQuery = window.matchMedia('(min-width: 901px)')
  const defaultEditorHeight = 360
  const minimumEditorHeight = 240
  const maximumEditorHeight = 1800
  const defaultTaskListWidth = 320
  const layoutModeCodes = {
    auto: 'AUTO',
    sideBySide: 'SIDE_BY_SIDE',
    stacked: 'STACKED'
  }

  let lookups = null
  let tasks = []
  let currentTask = null
  let currentView = 'active'
  let isEditorReady = false
  let isDirty = false
  let cleanTaskSnapshot = null
  let preferredBodyFormatCode = 'HTML'
  let preferredMarkdownEditType = 'MARKDOWN'
  let preferredEditorHeight = defaultEditorHeight
  let lookupSettings = null
  let activeLookupSettingsGroup = 'taskTypes'
  let editingLookupCode = null
  let layoutPreference = {
    taskListWidth: defaultTaskListWidth,
    taskListHeight: null,
    layoutMode: layoutModeCodes.auto
  }
  let layoutPreferenceSaveTimer = null
  let unsavedChangesDialogResolve = null
  let completeWaitDialogResolve = null
  let dirtyTrackingSuppressions = 0
  let markdownEditTypeSwitchCleanUntil = 0
  let markdownEditTypeSwitchWasClean = false

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

  function getColorInputValue(value, fallback) {
    return normalizeBadgeColor(value) || fallback
  }

  function renderBadge(label, backgroundColor, foregroundColor) {
    const safeBackground = normalizeBadgeColor(backgroundColor)
    const safeForeground = normalizeBadgeColor(foregroundColor)
    const style = safeBackground && safeForeground
      ? ` style="background-color: ${safeBackground}; color: ${safeForeground};"`
      : ''

    return `<span class="task-badge"${style}>${encodeText(label)}</span>`
  }

  function renderTaskStatusBadge(task) {
    if (task.taskStatusCode === 'ACTIVE') {
      return renderBadge(task.taskStatusName, '#6b7280', '#ffffff')
    }

    return renderBadge(task.taskStatusName, task.taskStatusBackgroundColor, task.taskStatusForegroundColor)
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

  function formatDateTime(value) {
    if (!value) {
      return ''
    }

    const date = new Date(value)
    if (Number.isNaN(date.getTime())) {
      return String(value)
    }

    return date.toLocaleString()
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

  function suppressDirtyTracking() {
    dirtyTrackingSuppressions += 1

    return function () {
      window.setTimeout(function () {
        dirtyTrackingSuppressions = Math.max(0, dirtyTrackingSuppressions - 1)
      }, 0)
    }
  }

  function suppressDirtyTrackingFor(durationMs) {
    dirtyTrackingSuppressions += 1
    window.setTimeout(function () {
      dirtyTrackingSuppressions = Math.max(0, dirtyTrackingSuppressions - 1)
    }, durationMs)
  }

  function normalizeSnapshotValue(value) {
    return value == null ? '' : String(value)
  }

  function normalizeSnapshotTags(values) {
    return (values || [])
      .map(function (value) { return String(value).trim() })
      .filter(Boolean)
      .sort(function (left, right) { return left.localeCompare(right, undefined, { sensitivity: 'base' }) })
  }

  function createCurrentTaskSnapshot() {
    if (!currentTask || !isEditorReady) {
      return null
    }

    const payload = getTaskPayload()
    return {
      title: normalizeSnapshotValue(payload.title),
      taskTypeCode: normalizeSnapshotValue(payload.taskTypeCode),
      body: normalizeSnapshotValue(payload.body),
      bodyFormatCode: normalizeSnapshotValue(payload.bodyFormatCode),
      taskPriorityCode: normalizeSnapshotValue(payload.taskPriorityCode),
      taskSourceCode: normalizeSnapshotValue(payload.taskSourceCode),
      sourceReference: normalizeSnapshotValue(payload.sourceReference),
      sourceUrl: normalizeSnapshotValue(payload.sourceUrl),
      deadline: normalizeSnapshotValue(payload.deadline),
      activeWaitingForLabel: normalizeSnapshotValue(payload.activeWaitingForLabel),
      tags: normalizeSnapshotTags(payload.tags)
    }
  }

  function snapshotsMatch(left, right) {
    return JSON.stringify(left) === JSON.stringify(right)
  }

  function setCleanTaskSnapshot() {
    cleanTaskSnapshot = createCurrentTaskSnapshot()
  }

  function markDirty() {
    if (!isEditorReady || dirtyTrackingSuppressions > 0 || Date.now() < markdownEditTypeSwitchCleanUntil) {
      return
    }

    const currentSnapshot = createCurrentTaskSnapshot()
    if (cleanTaskSnapshot && currentSnapshot && snapshotsMatch(cleanTaskSnapshot, currentSnapshot)) {
      isDirty = false
      setStatus(currentTask && currentTask.id ? 'Loaded' : 'Draft', 'ready')
      return
    }

    clearValidationState()
    isDirty = true
    setStatus('Unsaved changes', 'dirty')
  }

  function restoreCleanAfterMarkdownEditTypeSwitch() {
    if (!markdownEditTypeSwitchWasClean || !currentTask) {
      return
    }

    setCleanTaskSnapshot()
    isDirty = false
    window.Editor.markClean()
    setStatus(currentTask.id ? 'Loaded' : 'Draft', 'ready')
  }

  function preserveCleanStateDuringMarkdownEditTypeSwitch() {
    if (!currentTask) {
      return
    }

    if (Date.now() < markdownEditTypeSwitchCleanUntil) {
      markdownEditTypeSwitchCleanUntil = Date.now() + 5000
      suppressDirtyTrackingFor(5000)
      if (markdownEditTypeSwitchWasClean) {
        restoreCleanAfterMarkdownEditTypeSwitch()
      }
      return
    }

    markdownEditTypeSwitchWasClean = !isDirty
    markdownEditTypeSwitchCleanUntil = Date.now() + 5000
    suppressDirtyTrackingFor(5000)

    if (!markdownEditTypeSwitchWasClean) {
      return
    }

    restoreCleanAfterMarkdownEditTypeSwitch()
    ;[0, 250, 1000, 2500, 5000].forEach(function (delay) {
      window.setTimeout(restoreCleanAfterMarkdownEditTypeSwitch, delay)
    })
  }

  function hasUnsavedChanges() {
    return !!(currentTask && isDirty)
  }

  function resolveUnsavedChangesDialog(choice) {
    if (!unsavedChangesDialogResolve) {
      return
    }

    const resolve = unsavedChangesDialogResolve
    unsavedChangesDialogResolve = null
    resolve(choice)
  }

  function setUnsavedChangesDialogBusy(isBusy) {
    $('#unsaved-save-button')
      .prop('disabled', isBusy)
      .text(isBusy ? 'Saving' : 'Save')
    $('#unsaved-discard-button, #unsaved-cancel-button').prop('disabled', isBusy)
  }

  function closeUnsavedChangesDialog() {
    setUnsavedChangesDialogBusy(false)
    $('#unsaved-changes-overlay').prop('hidden', true)
  }

  function showUnsavedChangesDialog() {
    return new Promise(function (resolve) {
      unsavedChangesDialogResolve = resolve
      setUnsavedChangesDialogBusy(false)
      $('#unsaved-changes-overlay').prop('hidden', false)
      $('#unsaved-save-button').trigger('focus')
    })
  }

  async function allowContextSwitch() {
    if (!hasUnsavedChanges()) {
      return true
    }

    const choice = await showUnsavedChangesDialog()
    if (choice === 'cancel') {
      closeUnsavedChangesDialog()
      return false
    }

    if (choice === 'discard') {
      closeUnsavedChangesDialog()
      isDirty = false
      setStatus('Changes discarded', 'ready')
      return true
    }

    try {
      setUnsavedChangesDialogBusy(true)
      const saved = await saveTask()
      if (saved) {
        closeUnsavedChangesDialog()
        return true
      }

      closeUnsavedChangesDialog()
      return false
    } catch (error) {
      closeUnsavedChangesDialog()
      setStatus(getErrorMessage(error, 'Could not save task'), 'error')
      return false
    }
  }

  function resolveCompleteWaitDialog(choice) {
    if (!completeWaitDialogResolve) {
      return
    }

    const resolve = completeWaitDialogResolve
    completeWaitDialogResolve = null
    resolve(choice)
  }

  function closeCompleteWaitDialog() {
    $('#complete-wait-overlay').prop('hidden', true)
  }

  function showCompleteWaitDialog(waitingLabel) {
    return new Promise(function (resolve) {
      completeWaitDialogResolve = resolve
      $('#complete-wait-target').text(waitingLabel)
      $('#complete-wait-overlay').prop('hidden', false)
      $('#complete-wait-clear-button').trigger('focus')
    })
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

          <div id="task-list" class="task-list" aria-label="Tasks" tabindex="0"></div>
        </aside>

        <div id="layout-resizer" class="layout-resizer" role="separator" aria-label="Resize task list" aria-orientation="vertical" tabindex="0"></div>

        <section class="task-editor-panel" aria-labelledby="task-editor-title">
          <header class="editor-header">
            <div>
              <p id="task-status-label" class="eyebrow">No task selected</p>
              <h2 id="task-editor-title">Select or create a task</h2>
            </div>
            <div class="app-actions" aria-label="Task actions">
              <span id="save-status" class="save-status is-ready" role="status">Ready</span>
              <button id="settings-button" class="icon-button setup-button" type="button" aria-label="Setup" title="Setup">
                <svg class="button-icon" aria-hidden="true" viewBox="0 0 24 24" focusable="false">
                  <path d="M12 8.5a3.5 3.5 0 1 1 0 7 3.5 3.5 0 0 1 0-7Z"></path>
                  <path d="M19.4 15a1.8 1.8 0 0 0 .36 1.98l.04.04-2.12 2.12-.04-.04a1.8 1.8 0 0 0-1.98-.36 1.8 1.8 0 0 0-1.1 1.66V20.5h-3v-.1a1.8 1.8 0 0 0-1.1-1.66 1.8 1.8 0 0 0-1.98.36l-.04.04-2.12-2.12.04-.04A1.8 1.8 0 0 0 4.6 15a1.8 1.8 0 0 0-1.66-1.1H2.8v-3h.14A1.8 1.8 0 0 0 4.6 9.8a1.8 1.8 0 0 0-.36-1.98l-.04-.04 2.12-2.12.04.04a1.8 1.8 0 0 0 1.98.36 1.8 1.8 0 0 0 1.1-1.66V4.3h3v.1a1.8 1.8 0 0 0 1.1 1.66 1.8 1.8 0 0 0 1.98-.36l.04-.04 2.12 2.12-.04.04a1.8 1.8 0 0 0-.36 1.98 1.8 1.8 0 0 0 1.66 1.1h.14v3h-.14A1.8 1.8 0 0 0 19.4 15Z"></path>
                </svg>
                <span>Setup</span>
              </button>
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
              <label class="field-block waiting-field" for="waiting-text">
                <span>Waiting for</span>
                <input id="waiting-text" type="text" autocomplete="off" disabled>
              </label>
              <label class="field-block tags-field" for="task-tags">
                <span>Tags</span>
                <select id="task-tags" multiple disabled></select>
                <small class="field-help">Type a tag, then press Enter to add it.</small>
              </label>
            </div>

            <div class="body-header">
              <label class="field-label" for="text-body">Body</label>
            </div>
            <div id="editor-host" class="editor-host">
              <textarea id="text-body"></textarea>
            </div>

            <div class="source-grid">
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

            <section class="attachments-section" aria-labelledby="attachments-title">
              <div class="attachments-header">
                <h3 id="attachments-title">Attachments</h3>
                <div class="attachment-actions">
                  <select id="attachment-kind" aria-label="Attachment kind" disabled></select>
                  <input id="attachment-file" class="sr-only" type="file" disabled>
                  <button id="attachment-add-button" class="secondary-button" type="button" disabled>Add file</button>
                </div>
              </div>
              <div id="attachment-list" class="attachment-list"><span class="empty-attachments">No attachments.</span></div>
            </section>

            <section class="timeline-section" aria-labelledby="timeline-title">
              <div class="timeline-header">
                <h3 id="timeline-title">Timeline</h3>
              </div>
              <div id="timeline-list" class="timeline-list" aria-live="polite">
                <div class="empty-timeline">No timeline.</div>
              </div>
              <div id="comment-form" class="comment-form">
                <label class="sr-only" for="comment-text">Comment</label>
                <textarea id="comment-text" rows="2" placeholder="Comment" disabled></textarea>
                <button id="comment-add-button" class="secondary-button" type="button" disabled>Add</button>
              </div>
            </section>
          </form>
        </section>

        <div id="settings-overlay" class="modal-overlay" hidden>
          <section class="settings-dialog setup-dialog" role="dialog" aria-modal="true" aria-labelledby="settings-title">
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

            <label class="settings-field" for="editor-height">
              <span>Editor height (px)</span>
              <input id="editor-height" type="number" min="${minimumEditorHeight}" max="${maximumEditorHeight}" step="1" disabled>
            </label>

            <label class="settings-field" for="layout-mode">
              <span>Task layout</span>
              <select id="layout-mode">
                <option value="AUTO">Auto</option>
                <option value="SIDE_BY_SIDE">Side by side</option>
                <option value="STACKED">Stacked</option>
              </select>
            </label>

            <section class="lookup-settings-panel" aria-labelledby="lookup-settings-title">
              <h3 id="lookup-settings-title">Lookup values</h3>
              <div id="lookup-settings-groups" class="lookup-group-buttons" aria-label="Lookup groups"></div>
            </section>
          </section>
        </div>

        <div id="lookup-list-overlay" class="modal-overlay" hidden>
          <section class="settings-dialog lookup-list-dialog" role="dialog" aria-modal="true" aria-labelledby="lookup-list-title">
            <header class="settings-header">
              <h2 id="lookup-list-title">Lookup values</h2>
              <button id="lookup-list-close-button" class="icon-button" type="button" aria-label="Close lookup list" title="Close">&times;</button>
            </header>

            <div id="lookup-list-items" class="lookup-list-items"></div>

            <div class="modal-actions">
              <button id="lookup-list-done-button" class="secondary-button" type="button">Close</button>
              <button id="lookup-list-new-button" type="button">New</button>
            </div>
          </section>
        </div>

        <div id="lookup-edit-overlay" class="modal-overlay" hidden>
          <section class="settings-dialog lookup-edit-dialog" role="dialog" aria-modal="true" aria-labelledby="lookup-edit-title">
            <header class="settings-header">
              <h2 id="lookup-edit-title">Lookup value</h2>
            </header>

            <div class="lookup-edit-grid">
              <label class="settings-field" for="lookup-edit-code">
                <span>Code</span>
                <input id="lookup-edit-code" type="text" autocomplete="off">
              </label>
              <label class="settings-field" for="lookup-edit-name">
                <span>Name</span>
                <input id="lookup-edit-name" type="text" autocomplete="off" required>
              </label>
              <label class="settings-field lookup-description-edit" for="lookup-edit-description">
                <span>Description</span>
                <input id="lookup-edit-description" type="text" autocomplete="off">
              </label>
              <label class="settings-field" for="lookup-edit-background-color">
                <span>Background</span>
                <input id="lookup-edit-background-color" type="color">
              </label>
              <label class="settings-field" for="lookup-edit-foreground-color">
                <span>Text</span>
                <input id="lookup-edit-foreground-color" type="color">
              </label>
            </div>

            <div class="lookup-edit-options">
              <label class="lookup-check">
                <input id="lookup-edit-is-active" type="checkbox">
                <span>Active</span>
              </label>
              <label class="lookup-check">
                <input id="lookup-edit-is-selected" type="checkbox">
                <span>Default</span>
              </label>
              <span id="lookup-edit-preview" class="lookup-edit-preview"></span>
            </div>

            <p id="lookup-edit-error" class="form-error" hidden></p>

            <div class="modal-actions">
              <button id="lookup-edit-delete-button" class="secondary-button danger-button lookup-delete-button" type="button" hidden>Delete</button>
              <button id="lookup-edit-cancel-button" class="secondary-button" type="button">Cancel</button>
              <button id="lookup-edit-save-button" type="button">Save</button>
            </div>
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

        <div id="unsaved-changes-overlay" class="modal-overlay" hidden>
          <section class="settings-dialog unsaved-dialog" role="dialog" aria-modal="true" aria-labelledby="unsaved-changes-title">
            <header class="settings-header">
              <h2 id="unsaved-changes-title">Unsaved changes</h2>
            </header>

            <p class="settings-help">Save your changes before switching context?</p>

            <div class="modal-actions">
              <button id="unsaved-cancel-button" class="secondary-button" type="button">Cancel</button>
              <button id="unsaved-discard-button" class="secondary-button danger-button" type="button">Discard</button>
              <button id="unsaved-save-button" type="button">Save</button>
            </div>
          </section>
        </div>

        <div id="complete-wait-overlay" class="modal-overlay" hidden>
          <section class="settings-dialog unsaved-dialog" role="dialog" aria-modal="true" aria-labelledby="complete-wait-title">
            <header class="settings-header">
              <h2 id="complete-wait-title">Clear waiting?</h2>
            </header>

            <p class="settings-help">This task is waiting for <strong id="complete-wait-target"></strong>. Completing it will clear the waiting target.</p>

            <div class="modal-actions">
              <button id="complete-wait-cancel-button" class="secondary-button" type="button">Cancel</button>
              <button id="complete-wait-clear-button" type="button">Clear waiting and complete</button>
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
    renderLookupOptions('#attachment-kind', lookups.attachmentKinds, true)
    renderTagOptions(lookups.tags || [])
  }

  function formatFileSize(size) {
    if (size < 1024) return `${size} B`
    if (size < 1024 * 1024) return `${Math.round(size / 1024)} KB`
    return `${(size / (1024 * 1024)).toFixed(1)} MB`
  }

  function renderAttachments(items) {
    if (!items || items.length === 0) {
      $('#attachment-list').html('<span class="empty-attachments">No attachments.</span>')
      return
    }

    $('#attachment-list').html(items.map(function (item) {
      return `<div class="attachment-row">
        <button class="attachment-download-button attachment-name" type="button" data-attachment-id="${item.id}">${encodeText(item.fileName)}</button>
        <span>${encodeText(item.attachmentKindName || '')}</span>
        <span>${formatFileSize(item.fileSize)}</span>
        <button class="attachment-delete-button secondary-button danger-button" type="button" data-attachment-id="${item.id}" aria-label="Remove ${encodeAttribute(item.fileName)}">Remove</button>
      </div>`
    }).join(''))
  }

  async function loadAttachments(taskId) {
    if (!taskId) {
      renderAttachments([])
      return
    }
    renderAttachments(await sendBridgeMessage('task.attachment.list', { taskId }))
  }

  function readFileAsBase64(file) {
    return new Promise(function (resolve, reject) {
      const reader = new FileReader()
      reader.onload = function () { resolve(reader.result.toString().split(',')[1] || '') }
      reader.onerror = function () { reject(new Error('Could not read the selected file.')) }
      reader.readAsDataURL(file)
    })
  }

  async function addAttachment() {
    if (!currentTask || !currentTask.id) return
    const file = $('#attachment-file')[0].files[0]
    if (!file) return
    if (file.size > 25 * 1024 * 1024) throw new Error('Attachments cannot exceed 25 MB.')

    $('#attachment-add-button').prop('disabled', true).text('Adding')
    try {
      const items = await sendBridgeMessage('task.attachment.create', {
        taskId: currentTask.id,
        fileName: file.name,
        contentType: file.type || null,
        base64Data: await readFileAsBase64(file),
        attachmentKindCode: $('#attachment-kind').val().toString() || null,
        description: null
      })
      renderAttachments(items)
      $('#attachment-file').val('')
      await loadTimeline(currentTask.id)
      setStatus('Attachment added', 'saved')
    } finally {
      $('#attachment-add-button').prop('disabled', false).text('Add file')
    }
  }

  async function downloadAttachment(attachmentId) {
    const item = await sendBridgeMessage('task.attachment.get', { attachmentId })
    const bytes = Uint8Array.from(atob(item.base64Data), function (character) { return character.charCodeAt(0) })
    const url = URL.createObjectURL(new Blob([bytes], { type: item.contentType }))
    const link = document.createElement('a')
    link.href = url
    link.download = item.fileName
    link.click()
    window.setTimeout(function () { URL.revokeObjectURL(url) }, 1000)
  }

  async function deleteAttachment(attachmentId) {
    const items = await sendBridgeMessage('task.attachment.delete', {
      taskId: currentTask.id,
      attachmentId
    })
    renderAttachments(items)
    await loadTimeline(currentTask.id)
    setStatus('Attachment removed', 'saved')
  }

  function renderTagOptions(values) {
    const $tags = $('#task-tags')
    if ($tags.hasClass('select2-hidden-accessible')) {
      $tags.select2('destroy')
    }

    $tags.html((values || []).map(function (value) {
      const encoded = encodeAttribute(value)
      return `<option value="${encoded}">${encodeText(value)}</option>`
    }).join(''))

    $tags.select2({
      tags: true,
      width: '100%',
      placeholder: 'Start typing a tag...',
      dropdownParent: $('.tags-field')
    })
  }

  function setTaskTags(values) {
    const $tags = $('#task-tags')
    ;(values || []).forEach(function (value) {
      if (!$tags.find('option').filter(function () { return this.value === value }).length) {
        $tags.append(new Option(value, value, false, false))
      }
    })
    $tags.val(values || []).trigger('change.select2')
  }

  function renderLookupSettings() {
    $('#lookup-settings-groups').html(Object.keys(lookupSettingsGroups).map(function (group) {
      const count = lookupSettings && lookupSettings[group] ? lookupSettings[group].length : 0
      const countLabel = lookupSettings ? `<span>${count}</span>` : ''
      return `<button class="lookup-group-button secondary-button" type="button" data-lookup-group="${group}">${lookupSettingsGroups[group]}${countLabel}</button>`
    }).join(''))
  }

  async function loadLookupSettings() {
    $('#lookup-settings-groups').html('<div class="empty-lookup-settings">Loading lookup values.</div>')
    lookupSettings = await sendBridgeMessage('lookup.settings.get', {})
    renderLookupSettings()
  }

  function getActiveLookupItems() {
    return lookupSettings && lookupSettings[activeLookupSettingsGroup]
      ? lookupSettings[activeLookupSettingsGroup]
      : []
  }

  function findActiveLookupItem(code) {
    return getActiveLookupItems().find(function (item) {
      return item.code === code
    }) || null
  }

  function renderLookupList() {
    $('#lookup-list-title').text(lookupSettingsGroups[activeLookupSettingsGroup])
    $('#lookup-list-new-button').text(`New ${lookupSettingsGroupNouns[activeLookupSettingsGroup]}`)

    const items = getActiveLookupItems()
    if (items.length === 0) {
      $('#lookup-list-items').html('<div class="empty-lookup-settings">No lookup values.</div>')
      return
    }

    $('#lookup-list-items').html(items.map(function (item, index) {
      const backgroundColor = getColorInputValue(item.backgroundColor, '#6b7280')
      const foregroundColor = getColorInputValue(item.foregroundColor, '#ffffff')
      const activeText = item.isActive ? 'Active' : 'Inactive'
      const selectedText = item.isSelected ? '<span class="lookup-state-pill">Default</span>' : ''
      const systemText = item.isSystem ? '<span class="lookup-state-pill">System</span>' : ''
      const usedText = !item.canDelete && !item.isSystem ? '<span class="lookup-state-pill">Used</span>' : ''
      const encodedCode = encodeAttribute(item.code)
      const encodedName = encodeAttribute(item.name)

      return `
        <article class="lookup-list-row">
          <div class="lookup-list-main">
            <span class="lookup-preview">${renderBadge(item.name, backgroundColor, foregroundColor)}</span>
            <span class="lookup-code">${encodeText(item.code)}</span>
            <span class="lookup-list-description">${encodeText(item.description || '')}</span>
          </div>
          <div class="lookup-list-meta">
            <span>${activeText}</span>
            ${selectedText}
            ${systemText}
            ${usedText}
          </div>
          <button class="lookup-list-edit-button secondary-button" type="button" data-code="${encodedCode}">Edit</button>
          <div class="lookup-list-order" aria-label="Order ${encodedName}">
            <button class="lookup-reorder-button" type="button" data-code="${encodedCode}" data-direction="up" title="Move up" aria-label="Move ${encodedName} up"${index === 0 ? ' disabled' : ''}>&uarr;</button>
            <button class="lookup-reorder-button" type="button" data-code="${encodedCode}" data-direction="down" title="Move down" aria-label="Move ${encodedName} down"${index === items.length - 1 ? ' disabled' : ''}>&darr;</button>
          </div>
        </article>
      `
    }).join(''))
  }

  async function reorderLookupItem(code, direction) {
    const items = getActiveLookupItems()
    const currentIndex = items.findIndex(function (item) {
      return item.code === code
    })
    const offset = direction === 'up' ? -1 : 1
    const nextIndex = currentIndex + offset

    if (currentIndex < 0 || nextIndex < 0 || nextIndex >= items.length) {
      return
    }

    const orderedCodes = items.map(function (item) {
      return item.code
    })
    const movedCode = orderedCodes[currentIndex]
    orderedCodes[currentIndex] = orderedCodes[nextIndex]
    orderedCodes[nextIndex] = movedCode

    $('.lookup-reorder-button').prop('disabled', true)
    setStatus('Saving lookup order', 'ready')

    try {
      lookupSettings = await sendBridgeMessage('lookup.settings.reorder', {
        group: activeLookupSettingsGroup,
        orderedCodes
      })

      renderLookupSettings()
      renderLookupList()
      await refreshLookupDependentUi()
      setStatus('Lookup order saved', 'saved')
      window.setTimeout(function () {
        $('#lookup-list-items .lookup-reorder-button')
          .filter(function () {
            return $(this).attr('data-code') === code && $(this).attr('data-direction') === direction
          })
          .trigger('focus')
      }, 0)
    } catch (error) {
      renderLookupList()
      setStatus(getErrorMessage(error, 'Could not save lookup order'), 'error')
    }
  }

  async function openLookupList(group) {
    activeLookupSettingsGroup = group
    if (!lookupSettings) {
      lookupSettings = await sendBridgeMessage('lookup.settings.get', {})
      renderLookupSettings()
    }

    renderLookupList()
    $('#lookup-list-overlay').prop('hidden', false)
    $('#lookup-list-new-button').trigger('focus')
  }

  function closeLookupList() {
    $('#lookup-list-overlay').prop('hidden', true)
    $('#lookup-settings-groups [data-lookup-group="' + activeLookupSettingsGroup + '"]').trigger('focus')
  }

  function openLookupEdit(code) {
    editingLookupCode = code || null
    const item = editingLookupCode ? findActiveLookupItem(editingLookupCode) : null

    $('#lookup-edit-title').text(editingLookupCode
      ? `Edit ${lookupSettingsGroupNouns[activeLookupSettingsGroup]}`
      : `New ${lookupSettingsGroupNouns[activeLookupSettingsGroup]}`)
    $('#lookup-edit-code')
      .val(item ? item.code : '')
      .prop('disabled', !!item)
      .removeClass('is-invalid')
    $('#lookup-edit-name').val(item ? item.name : '').removeClass('is-invalid')
    $('#lookup-edit-description').val(item && item.description ? item.description : '')
    $('#lookup-edit-is-active')
      .prop('checked', item ? item.isActive : true)
      .prop('disabled', !!(item && activeLookupSettingsGroup === 'taskStatuses' && item.isSystem))
    $('#lookup-edit-is-selected').prop('checked', item ? item.isSelected : false)
    $('#lookup-edit-background-color').val(getColorInputValue(item && item.backgroundColor, '#6b7280'))
    $('#lookup-edit-foreground-color').val(getColorInputValue(item && item.foregroundColor, '#ffffff'))
    $('#lookup-edit-error').prop('hidden', true).text('')
    $('#lookup-edit-delete-button')
      .prop('hidden', !(item && item.canDelete))
      .prop('disabled', false)
    $('#lookup-edit-save-button, #lookup-edit-cancel-button').prop('disabled', false)
    updateLookupEditPreview()

    $('#lookup-edit-overlay').prop('hidden', false)
    window.setTimeout(function () {
      const field = editingLookupCode ? $('#lookup-edit-name')[0] : $('#lookup-edit-code')[0]
      field.focus()
      field.select()
    }, 0)
  }

  function closeLookupEdit() {
    $('#lookup-edit-overlay').prop('hidden', true)
    $('#lookup-list-new-button').trigger('focus')
  }

  function updateLookupEditPreview() {
    const label = $('#lookup-edit-name').val().toString().trim()
      || $('#lookup-edit-code').val().toString().trim()
      || 'Preview'
    $('#lookup-edit-preview').html(renderBadge(
      label,
      $('#lookup-edit-background-color').val().toString(),
      $('#lookup-edit-foreground-color').val().toString()))
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

  function getNextLookupSortOrder() {
    return getActiveLookupItems().reduce(function (maxSort, current) {
      return Math.max(maxSort, Number(current.sortOrder) || 0)
    }, 0) + 10
  }

  function getSupportedMarkdownEditType(value) {
    return String(value || '').toUpperCase() === 'WYSIWYG' ? 'WYSIWYG' : 'MARKDOWN'
  }

  function getSupportedEditorHeight(value) {
    const height = Math.round(Number(value))
    if (!Number.isFinite(height)) {
      return defaultEditorHeight
    }

    return Math.min(maximumEditorHeight, Math.max(minimumEditorHeight, height))
  }

  async function loadEditorPreference() {
    const preference = await sendBridgeMessage('editor.preference.get', {})
    preferredBodyFormatCode = getSupportedBodyFormatCode(preference.bodyFormatCode)
    preferredMarkdownEditType = getSupportedMarkdownEditType(preference.markdownEditType)
    preferredEditorHeight = getSupportedEditorHeight(preference.editorHeight)
    $('#editor-mode').val(preferredBodyFormatCode)
    $('#editor-mode').prop('disabled', false)
    $('#editor-height').val(preferredEditorHeight)
    $('#editor-height').prop('disabled', false)
  }

  function saveEditorPreference(bodyFormatCode, markdownEditType, editorHeight) {
    preferredBodyFormatCode = getSupportedBodyFormatCode(bodyFormatCode || preferredBodyFormatCode)
    preferredMarkdownEditType = getSupportedMarkdownEditType(markdownEditType || preferredMarkdownEditType)
    preferredEditorHeight = getSupportedEditorHeight(editorHeight == null ? preferredEditorHeight : editorHeight)

    return sendBridgeMessage('editor.preference.save', {
      bodyFormatCode: preferredBodyFormatCode,
      markdownEditType: preferredMarkdownEditType,
      editorHeight: preferredEditorHeight
    })
  }

  function saveMarkdownEditTypePreference(markdownEditType) {
    const nextMarkdownEditType = getSupportedMarkdownEditType(markdownEditType)
    if (nextMarkdownEditType === preferredMarkdownEditType) {
      return Promise.resolve()
    }

    preferredMarkdownEditType = nextMarkdownEditType

    return sendBridgeMessage('editor.preference.save', {
      bodyFormatCode: preferredBodyFormatCode,
      markdownEditType: preferredMarkdownEditType,
      editorHeight: preferredEditorHeight
    })
  }

  function saveEditorHeightPreferenceFromSettings() {
    const heightInput = $('#editor-height')
    if (heightInput.prop('disabled')) {
      return Promise.resolve()
    }

    const height = heightInput.val()
    const nextHeight = getSupportedEditorHeight(height)
    heightInput.val(nextHeight)

    if (nextHeight === preferredEditorHeight) {
      return Promise.resolve()
    }

    return saveEditorPreference(preferredBodyFormatCode, preferredMarkdownEditType, nextHeight)
      .then(function () {
        applyEditorHeightPreference()
        setStatus('Editor height preference saved', 'saved')
      })
  }

  function applyEditorHeightPreference() {
    if (!window.Editor || typeof window.Editor.setHeight !== 'function' || !isEditorReady) {
      return
    }

    window.Editor.setHeight(preferredEditorHeight)
  }

  function renderEmptyEditor() {
    currentTask = null
    isDirty = false
    isEditorReady = false
    cleanTaskSnapshot = null
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
    setTaskTags([])
    $('#editor-mode').val(getSupportedBodyFormatCode(preferredBodyFormatCode))
    $('#waiting-text').val('')
    $('#task-form input, #task-form select').prop('disabled', true)
    $('#complete-button, #cancel-button, #save-button').prop('disabled', true)
    $('#editor-mode').prop('disabled', !lookups)
    $('#editor-host').html('<div class="empty-editor">Select a task to edit the body.</div>')
    $('#comment-text').val('').removeClass('is-invalid')
    setCommentControlsEnabled(false)
    renderAttachments([])
    renderTimeline([])
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

  function getLayoutPreferenceValue(key, fallback) {
    const value = Number(layoutPreference[key])
    return Number.isFinite(value) && value > 0 ? value : fallback
  }

  function normalizeLayoutMode(layoutMode) {
    const normalizedLayoutMode = String(layoutMode || '').trim().toUpperCase()

    if (normalizedLayoutMode === layoutModeCodes.sideBySide || normalizedLayoutMode === layoutModeCodes.stacked) {
      return normalizedLayoutMode
    }

    return layoutModeCodes.auto
  }

  function saveLayoutPreference() {
    if (layoutPreferenceSaveTimer) {
      window.clearTimeout(layoutPreferenceSaveTimer)
      layoutPreferenceSaveTimer = null
    }

    return sendBridgeMessage('layout.preference.save', {
      taskListWidth: layoutPreference.taskListWidth,
      taskListHeight: layoutPreference.taskListHeight,
      layoutMode: layoutPreference.layoutMode
    }).then(function () {
      return true
    }).catch(function (error) {
      setStatus(getErrorMessage(error, 'Could not save layout preference'), 'error')
      return false
    })
  }

  function scheduleLayoutPreferenceSave() {
    if (layoutPreferenceSaveTimer) {
      window.clearTimeout(layoutPreferenceSaveTimer)
    }

    layoutPreferenceSaveTimer = window.setTimeout(saveLayoutPreference, 250)
  }

  async function loadLayoutPreference() {
    const preference = await sendBridgeMessage('layout.preference.get', {})
    layoutPreference.taskListWidth = preference.taskListWidth || defaultTaskListWidth
    layoutPreference.taskListHeight = preference.taskListHeight || Math.round(window.innerHeight * 0.42)
    layoutPreference.layoutMode = normalizeLayoutMode(preference.layoutMode)
    applyStoredLayoutSplit(false)
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

  function setTaskListWidth(width, shouldSave) {
    const bounds = getLayoutBounds()
    const clampedWidth = clampSplitValue(width, 220, Math.max(260, bounds.width - 460))
    document.documentElement.style.setProperty('--task-list-width', `${clampedWidth}px`)
    layoutPreference.taskListWidth = Math.round(clampedWidth)

    if (shouldSave) {
      scheduleLayoutPreferenceSave()
    }
  }

  function setTaskListHeight(height, shouldSave) {
    const bounds = getLayoutBounds()
    const clampedHeight = clampSplitValue(height, 170, Math.max(220, bounds.height - 380))
    document.documentElement.style.setProperty('--task-list-height', `${clampedHeight}px`)
    layoutPreference.taskListHeight = Math.round(clampedHeight)

    if (shouldSave) {
      scheduleLayoutPreferenceSave()
    }
  }

  function isWideLayout() {
    if (layoutPreference.layoutMode === layoutModeCodes.sideBySide) {
      return true
    }

    if (layoutPreference.layoutMode === layoutModeCodes.stacked) {
      return false
    }

    return wideLayoutMediaQuery.matches
  }

  function applyLayoutMode() {
    $('html')
      .toggleClass('layout-mode-side-by-side', layoutPreference.layoutMode === layoutModeCodes.sideBySide)
      .toggleClass('layout-mode-stacked', layoutPreference.layoutMode === layoutModeCodes.stacked)
    $('#layout-mode').val(layoutPreference.layoutMode)
  }

  function applyStoredLayoutSplit(shouldSave) {
    applyLayoutMode()
    setTaskListWidth(getLayoutPreferenceValue('taskListWidth', defaultTaskListWidth), shouldSave)
    setTaskListHeight(getLayoutPreferenceValue('taskListHeight', Math.round(window.innerHeight * 0.42)), shouldSave)
    $('#layout-resizer').attr('aria-orientation', isWideLayout() ? 'vertical' : 'horizontal')
  }

  function switchLayoutMode() {
    layoutPreference.layoutMode = normalizeLayoutMode($('#layout-mode').val())
    applyStoredLayoutSplit(false)
    saveLayoutPreference().then(function (wasSaved) {
      if (wasSaved) {
        setStatus('Layout preference saved', 'saved')
      }
    })
  }

  function bindLayoutResizer() {
    const $resizer = $('#layout-resizer')

    applyStoredLayoutSplit(false)

    const onMediaChange = function () {
      applyStoredLayoutSplit(false)
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
          setTaskListWidth(moveEvent.clientX - shellRect.left, true)
        } else {
          setTaskListHeight(moveEvent.clientY - shellRect.top, true)
        }
      })

      $(document).on('pointerup.layoutResizer pointercancel.layoutResizer', function () {
        $resizer.removeClass('is-dragging')
        $(document).off('.layoutResizer')
        saveLayoutPreference()
      })
    })

    $resizer.on('keydown', function (event) {
      const step = event.shiftKey ? 40 : 20
      const currentWidth = getLayoutPreferenceValue('taskListWidth', defaultTaskListWidth)
      const currentHeight = getLayoutPreferenceValue('taskListHeight', Math.round(window.innerHeight * 0.42))

      if (isWideLayout() && (event.key === 'ArrowLeft' || event.key === 'ArrowRight')) {
        event.preventDefault()
        setTaskListWidth(currentWidth + (event.key === 'ArrowRight' ? step : -step), true)
      }

      if (!isWideLayout() && (event.key === 'ArrowUp' || event.key === 'ArrowDown')) {
        event.preventDefault()
        setTaskListHeight(currentHeight + (event.key === 'ArrowDown' ? step : -step), true)
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

  function getTaskSearchQuery() {
    return $('#task-search').val().toString().trim().toLowerCase()
  }

  function getVisibleTasks() {
    const query = getTaskSearchQuery()

    return query
      ? tasks.filter(function (task) {
        return task.title.toLowerCase().includes(query)
          || task.taskTypeName.toLowerCase().includes(query)
          || task.taskStatusName.toLowerCase().includes(query)
          || (task.taskPriorityName || '').toLowerCase().includes(query)
          || (task.activeWaitingForLabel || '').toLowerCase().includes(query)
      })
      : tasks
  }

  function renderTaskList() {
    const query = getTaskSearchQuery()
    const visibleTasks = getVisibleTasks()

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
            ${renderTaskStatusBadge(task)}
            ${priority}
            ${deadline}
            ${waiting}
          </span>
        </button>
      `
    }).join(''))
  }

  function focusTaskRow(taskId) {
    window.setTimeout(function () {
      $(`#task-list .task-row[data-task-id="${taskId}"]`).trigger('focus')
    }, 0)
  }

  function focusTaskList() {
    window.setTimeout(function () {
      $('#task-list').trigger('focus')
    }, 0)
  }

  function focusTaskSearchWithKey(key) {
    const search = $('#task-search')
    const currentValue = search.val().toString()
    search.val(currentValue + key).trigger('input')
    search.trigger('focus')

    const element = search[0]
    if (element && typeof element.setSelectionRange === 'function') {
      const position = element.value.length
      element.setSelectionRange(position, position)
    }
  }

  function isTextEntryKey(event) {
    return event.key.length === 1 && !event.altKey && !event.ctrlKey && !event.metaKey
  }

  function selectVisibleTaskByIndex(index) {
    const visibleTasks = getVisibleTasks()
    if (visibleTasks.length === 0) {
      return
    }

    const clampedIndex = Math.max(0, Math.min(visibleTasks.length - 1, index))
    selectTaskWithUnsavedCheck(visibleTasks[clampedIndex].id)
  }

  function selectRelativeTask(offset) {
    const visibleTasks = getVisibleTasks()
    if (visibleTasks.length === 0) {
      return
    }

    const currentIndex = currentTask && currentTask.id
      ? visibleTasks.findIndex(function (task) {
        return task.id === currentTask.id
      })
      : -1
    const fallbackIndex = offset > 0 ? 0 : visibleTasks.length - 1
    const nextIndex = currentIndex < 0 ? fallbackIndex : currentIndex + offset

    selectVisibleTaskByIndex(nextIndex)
  }

  function selectTaskWithUnsavedCheck(taskId) {
    if (currentTask && currentTask.id === taskId) {
      focusTaskRow(taskId)
      return Promise.resolve()
    }

    return allowContextSwitch().then(function (isAllowed) {
      if (!isAllowed) {
        return null
      }

      return selectTask(taskId).then(function () {
        focusTaskRow(taskId)
      })
    }).catch(function (error) {
      setStatus(getErrorMessage(error, 'Could not load task'), 'error')
      return null
    })
  }

  function createNewTaskPayload(title) {
    const selectedTaskType = getSelectedLookupItem(lookups.taskTypes)
    const selectedTaskPriority = getSelectedLookupItem(lookups.taskPriorities)
    const bodyFormatCode = getSupportedBodyFormatCode(preferredBodyFormatCode)

    return {
      id: null,
      title,
      body: '',
      bodyFormatCode,
      taskTypeCode: selectedTaskType ? selectedTaskType.code : '',
      taskPriorityCode: selectedTaskPriority ? selectedTaskPriority.code : '',
      taskSourceCode: '',
      sourceReference: '',
      sourceUrl: '',
      deadline: null,
      activeWaitingFor: null,
      tags: [],
      taskStatusCode: 'ACTIVE',
      taskStatusName: 'Draft'
    }
  }

  function getSelectedLookupItem(items) {
    if (!items || !items.length) {
      return null
    }

    return items.find(function (item) {
      return item.isSelected === true
    }) || items[0]
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
      minHeight: preferredEditorHeight,
      markdownEditType: preferredMarkdownEditType,
      initialContent: initialContent || '',
      initialHtml: initialHtml || '',
      contentStyle:
        'body { color: #202124; font-family: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif; font-size: 15px; line-height: 1.55; }',
      onPickImage: pickEditorImage,
      onMarkdownEditTypeChanging: handleMarkdownEditTypeChanging,
      onMarkdownEditTypeChanged: handleMarkdownEditTypeChanged
    })

    if (typeof window.Editor.setHeight === 'function') {
      window.Editor.setHeight(preferredEditorHeight)
    }

    window.Editor.markClean()
    isEditorReady = true
    $('#save-button').prop('disabled', false)

    if (modeCode === 'MARKDOWN' && typeof window.Editor.getMarkdownEditType === 'function') {
      handleMarkdownEditTypeChanged(window.Editor.getMarkdownEditType())
    }
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

  function getVisibleWaitingLabel() {
    return $('#waiting-text').val().toString().trim()
  }

  function getCurrentWaitingLabel() {
    const visibleWaitingLabel = getVisibleWaitingLabel()
    if (visibleWaitingLabel) {
      return visibleWaitingLabel
    }

    return currentTask && currentTask.activeWaitingFor
      ? describeWaiting(currentTask.activeWaitingFor)
      : ''
  }

  function renderWaitingPanel(task) {
    const waitingFor = task.activeWaitingFor
    const canEditWaiting = !task.id || task.taskStatusCode === 'ACTIVE'

    $('#waiting-text').val(waitingFor ? describeWaiting(waitingFor) : '')

    $('#waiting-text').prop('disabled', !canEditWaiting)
  }

  function setCommentControlsEnabled(isEnabled) {
    $('#comment-text, #comment-add-button').prop('disabled', !isEnabled)
  }

  function formatDeadlineLogValue(value) {
    if (!value) return '(none)'
    const dateOnlyMatch = String(value).match(/^(\d{4})-(\d{2})-(\d{2})/)
    const parsed = dateOnlyMatch
      ? new Date(Number(dateOnlyMatch[1]), Number(dateOnlyMatch[2]) - 1, Number(dateOnlyMatch[3]))
      : new Date(value)
    if (Number.isNaN(parsed.getTime())) return value
    return new Intl.DateTimeFormat(undefined, {
      year: 'numeric',
      month: 'numeric',
      day: 'numeric'
    }).format(parsed)
  }

  function getTimelineText(item) {
    if (item.logTypeCode === 'DEADLINE_CHANGED') {
      return `Deadline changed from ${formatDeadlineLogValue(item.oldValue)} to ${formatDeadlineLogValue(item.newValue)}`
    }
    return item.text
  }

  function renderTimeline(items) {
    const timelineItems = Array.isArray(items) ? items : []

    if (timelineItems.length === 0) {
      $('#timeline-list').html('<div class="empty-timeline">No timeline.</div>')
      return
    }

    $('#timeline-list').html(timelineItems.map(function (item) {
      const kindLabel = item.kind === 'comment' ? 'Comment' : 'Log'
      const deleteButton = item.canDelete
        ? `<button class="timeline-delete-comment-button" type="button" data-comment-id="${item.id}" aria-label="Delete comment">Delete</button>`
        : ''

      return `
        <article class="timeline-entry timeline-entry-${encodeAttribute(item.kind)}">
          <span class="timeline-kind">${encodeText(kindLabel)}</span>
          <div class="timeline-entry-main">
            <p>${encodeText(getTimelineText(item))}</p>
            <time datetime="${encodeAttribute(item.createdAt)}">${encodeText(formatDateTime(item.createdAt))}</time>
          </div>
          ${deleteButton}
        </article>
      `
    }).join(''))
  }

  async function loadTimeline(taskId) {
    if (!taskId) {
      renderTimeline([])
      setCommentControlsEnabled(false)
      return
    }

    const items = await sendBridgeMessage('task.timeline.get', {
      taskId
    })
    renderTimeline(items)
    setCommentControlsEnabled(true)
  }

  async function addComment() {
    if (!currentTask || !currentTask.id) {
      return
    }

    const commentText = $('#comment-text').val().toString().trim()
    if (!commentText) {
      $('#comment-text').addClass('is-invalid').trigger('focus')
      setStatus('Comment is required', 'error')
      return
    }

    $('#comment-text').removeClass('is-invalid')
    $('#comment-add-button').prop('disabled', true).text('Adding')
    const wasDirty = isDirty

    try {
      const items = await sendBridgeMessage('task.comment.create', {
        taskId: currentTask.id,
        commentText
      })
      $('#comment-text').val('')
      renderTimeline(items)
      await loadTasks({ keepSelection: true })
      setStatus(wasDirty ? 'Unsaved changes' : 'Comment added', wasDirty ? 'dirty' : 'saved')
    } finally {
      $('#comment-add-button').prop('disabled', false).text('Add')
    }
  }

  async function deleteComment(commentId) {
    if (!currentTask || !currentTask.id || !commentId) {
      return
    }

    const wasDirty = isDirty
    const items = await sendBridgeMessage('task.comment.delete', {
      taskId: currentTask.id,
      commentId
    })
    renderTimeline(items)
    await loadTasks({ keepSelection: true })
    setStatus(wasDirty ? 'Unsaved changes' : 'Comment deleted', wasDirty ? 'dirty' : 'saved')
  }

  function restoreCurrentLookupFieldValues() {
    if (!currentTask) {
      return
    }

    $('#task-type').val(currentTask.taskTypeCode || '')
    $('#task-priority').val(currentTask.taskPriorityCode || '')
    $('#task-source').val(currentTask.taskSourceCode || '')
    setTaskTags(currentTask.tags || [])
    $('#editor-mode').val(getSupportedBodyFormatCode(preferredBodyFormatCode))
  }

  async function refreshLookupDependentUi() {
    lookups = await sendBridgeMessage('task.lookups.get', {})
    renderLookups()
    restoreCurrentLookupFieldValues()

    await loadTasks({ keepSelection: true })

    if (currentTask && currentTask.id && !hasUnsavedChanges()) {
      const task = await sendBridgeMessage('task.get', {
        id: currentTask.id
      })
      refreshCurrentTaskWithoutEditor(task)
    }
  }

  async function saveLookupEdit() {
    const code = $('#lookup-edit-code').val().toString().trim()
    const name = $('#lookup-edit-name').val().toString().trim()
    const item = editingLookupCode ? findActiveLookupItem(editingLookupCode) : null

    $('#lookup-edit-code, #lookup-edit-name').removeClass('is-invalid')

    if (!editingLookupCode && !code) {
      $('#lookup-edit-code').addClass('is-invalid').trigger('focus')
      $('#lookup-edit-error').text('Code is required.').prop('hidden', false)
      setStatus('Lookup code is required', 'error')
      return
    }

    if (!name) {
      $('#lookup-edit-name').addClass('is-invalid').trigger('focus')
      $('#lookup-edit-error').text('Name is required.').prop('hidden', false)
      setStatus('Lookup name is required', 'error')
      return
    }

    const $button = $('#lookup-edit-save-button')
    $button.prop('disabled', true).text('Saving')
    $('#lookup-edit-cancel-button').prop('disabled', true)

    const payload = {
      group: activeLookupSettingsGroup,
      code: editingLookupCode || code,
      name,
      description: $('#lookup-edit-description').val().toString().trim() || null,
      sortOrder: item ? item.sortOrder : getNextLookupSortOrder(),
      isActive: $('#lookup-edit-is-active').prop('checked'),
      isSelected: $('#lookup-edit-is-selected').prop('checked'),
      backgroundColor: $('#lookup-edit-background-color').val().toString(),
      foregroundColor: $('#lookup-edit-foreground-color').val().toString()
    }

    try {
      lookupSettings = await sendBridgeMessage(editingLookupCode ? 'lookup.settings.update' : 'lookup.settings.create', payload)

      renderLookupSettings()
      renderLookupList()
      closeLookupEdit()
      await refreshLookupDependentUi()
      setStatus('Lookup saved', 'saved')
    } finally {
      $button.prop('disabled', false).text('Save')
      $('#lookup-edit-cancel-button').prop('disabled', false)
    }
  }

  async function deleteLookupEdit() {
    if (!editingLookupCode) {
      return
    }

    const item = findActiveLookupItem(editingLookupCode)
    if (!item || !item.canDelete) {
      $('#lookup-edit-error').text('Only unused custom lookup values can be deleted.').prop('hidden', false)
      setStatus('Lookup value cannot be deleted', 'error')
      return
    }

    $('#lookup-edit-delete-button, #lookup-edit-save-button, #lookup-edit-cancel-button').prop('disabled', true)

    try {
      lookupSettings = await sendBridgeMessage('lookup.settings.delete', {
        group: activeLookupSettingsGroup,
        code: editingLookupCode
      })

      renderLookupSettings()
      renderLookupList()
      closeLookupEdit()
      await refreshLookupDependentUi()
      setStatus('Lookup deleted', 'saved')
    } finally {
      $('#lookup-edit-delete-button, #lookup-edit-save-button, #lookup-edit-cancel-button').prop('disabled', false)
    }
  }

  function renderTaskHeaderAndActions(task) {
    const isSavedTask = !!task.id
    const isFinal = task.taskStatusCode === 'COMPLETED' || task.taskStatusCode === 'CANCELLED'
    const canReopen = isSavedTask && isFinal
    const canCompleteOrCancel = isSavedTask && !isFinal

    $('#task-editor-title').text(task.id ? 'Task details' : 'New task')
    $('#task-status-label').text(task.taskStatusName || 'Draft')
    $('#complete-button')
      .text(canReopen ? 'Reopen' : 'Complete')
      .prop('disabled', !(canCompleteOrCancel || canReopen))
    $('#cancel-button').prop('disabled', !canCompleteOrCancel)
    $('#attachment-add-button, #attachment-kind, #attachment-file').prop('disabled', !isSavedTask)
    renderWaitingPanel(task)
  }

  function refreshCurrentTaskWithoutEditor(task) {
    const releaseDirtyTracking = suppressDirtyTracking()

    try {
      currentTask = task
      $('#task-title').val(task.title || '')
      $('#task-type').val(task.taskTypeCode || '')
      $('#task-priority').val(task.taskPriorityCode || '')
      $('#task-deadline').val(formatDate(task.deadline))
      $('#task-source').val(task.taskSourceCode || '')
      $('#task-source-reference').val(task.sourceReference || '')
      $('#task-source-url').val(task.sourceUrl || '')
      setTaskTags(task.tags || [])
      $('#editor-mode').val(getSupportedBodyFormatCode(preferredBodyFormatCode))
      $('#task-form input, #task-form select').prop('disabled', false)
      $('#editor-mode').prop('disabled', false)
      renderTaskHeaderAndActions(task)
      renderTaskList()
      setCleanTaskSnapshot()
    } finally {
      releaseDirtyTracking()
    }
  }

  async function renderTaskEditor(task) {
    const releaseDirtyTracking = suppressDirtyTracking()

    try {
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
      setTaskTags(task.tags || [])
      $('#editor-mode').val(getSupportedBodyFormatCode(preferredBodyFormatCode))
      renderWaitingPanel(task)

      $('#task-form input, #task-form select').prop('disabled', false)
      $('#editor-mode').prop('disabled', false)
      renderTaskHeaderAndActions(task)

      await initializeEditorForTask(task)
      await loadAttachments(task.id)
      await loadTimeline(task.id)
      isDirty = false
      setCleanTaskSnapshot()
      window.Editor.markClean()
      setStatus(task.id ? 'Loaded' : 'Draft', 'ready')
      renderTaskList()
    } finally {
      releaseDirtyTracking()
    }
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
      deadline: $('#task-deadline').val().toString() || null,
      activeWaitingForLabel: $('#waiting-text').val().toString().trim() || null,
      tags: $('#task-tags').val() || []
    }
  }

  async function switchEditorMode() {
    const targetModeCode = $('#editor-mode').val().toString() === 'MARKDOWN' ? 'MARKDOWN' : 'HTML'

    if (!currentTask || !isEditorReady) {
      $('#editor-mode').prop('disabled', true)

      try {
        await saveEditorPreference(targetModeCode)
        $('#editor-mode').val(preferredBodyFormatCode)
        setStatus('Editor preference saved', 'saved')
      } finally {
        $('#editor-mode').prop('disabled', false)
      }

      return
    }

    const activeModeCode = window.Editor.getMode() === 'markdown' ? 'MARKDOWN' : 'HTML'

    if (targetModeCode === activeModeCode) {
      return
    }

    const wasDirty = isDirty
    const releaseDirtyTracking = suppressDirtyTracking()
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
      await saveEditorPreference(targetModeCode).catch(function (error) {
        setStatus(getErrorMessage(error, 'Could not save editor preference'), 'error')
      })

      if (wasDirty) {
        isDirty = true
        setStatus('Unsaved changes', 'dirty')
      } else {
        isDirty = false
        setCleanTaskSnapshot()
        window.Editor.markClean()
        setStatus(currentTask.id ? 'Loaded' : 'Draft', 'ready')
      }
    } catch (error) {
      $('#editor-mode').val(activeModeCode).prop('disabled', false)
      isEditorReady = true
      setStatus(getErrorMessage(error, 'Could not switch editor'), 'error')
    } finally {
      releaseDirtyTracking()
    }
  }

  function handleMarkdownEditTypeChanged(markdownEditType) {
    preserveCleanStateDuringMarkdownEditTypeSwitch()
    saveMarkdownEditTypePreference(markdownEditType).catch(function (error) {
      setStatus(getErrorMessage(error, 'Could not save editor preference'), 'error')
    })
  }

  function handleMarkdownEditTypeChanging() {
    preserveCleanStateDuringMarkdownEditTypeSwitch()
  }

  async function loadTasks(options) {
    const keepSelection = options && options.keepSelection
    const selectFirst = options && options.selectFirst
    tasks = await sendBridgeMessage('task.list', {
      view: currentView
    })
    renderTaskList()

    if (keepSelection && currentTask && currentTask.id) {
      const stillVisible = tasks.some(function (task) {
        return task.id === currentTask.id
      })
      if (stillVisible) {
        return currentTask
      }
    }

    if (selectFirst) {
      if (tasks.length > 0) {
        await selectTask(tasks[0].id)
        focusTaskRow(tasks[0].id)
        return currentTask
      }

      renderEmptyEditor()
      focusTaskList()
      return null
    }

    renderTaskList()
    return null
  }

  async function selectTask(id) {
    const task = await sendBridgeMessage('task.get', {
      id
    })
    await renderTaskEditor(task)
  }

  async function saveTask() {
    if (!currentTask) {
      return false
    }

    const isNewTask = !currentTask.id
    clearValidationState()
    const payload = getTaskPayload()
    if (!payload.title) {
      setFieldInvalid('#task-title', true)
      setStatus('Title is required', 'error')
      $('#task-title').trigger('focus')
      return false
    }

    if (!payload.taskTypeCode) {
      setFieldInvalid('#task-type', true)
      setStatus('Task type is required', 'error')
      $('#task-type').trigger('focus')
      return false
    }

    setStatus('Saving', 'ready')
    $('#save-button').prop('disabled', true)
    const releaseDirtyTracking = suppressDirtyTracking()

    try {
      const savedTask = await sendBridgeMessage(currentTask.id ? 'task.update' : 'task.create', payload)
      if (currentTask.id) {
        refreshCurrentTaskWithoutEditor(savedTask)
        await loadTimeline(savedTask.id)
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
      return true
    } catch (error) {
      $('#save-button').prop('disabled', false)
      throw error
    } finally {
      releaseDirtyTracking()
    }
  }

  async function runLifecycleAction(type) {
    if (!currentTask || !currentTask.id) {
      return
    }

    const task = await sendBridgeMessage(type, {
      id: currentTask.id
    })
    refreshCurrentTaskWithoutEditor(task)
    await loadTimeline(task.id)
    selectViewForTask(task)
    await loadTasks({ keepSelection: true })
    setStatus('Updated', 'saved')
  }

  async function confirmCompleteWaitClear() {
    const waitingLabel = getCurrentWaitingLabel()

    if (!currentTask || currentTask.taskStatusCode !== 'ACTIVE' || !waitingLabel) {
      return true
    }

    const choice = await showCompleteWaitDialog(waitingLabel)
    closeCompleteWaitDialog()

    if (choice === 'clear') {
      $('#waiting-text').val('')
      return true
    }

    setStatus('Task remains active', 'ready')
    return false
  }

  async function runCompleteButtonAction() {
    const type = currentTask && (currentTask.taskStatusCode === 'COMPLETED' || currentTask.taskStatusCode === 'CANCELLED')
      ? 'task.reopen'
      : 'task.complete'

    if (type === 'task.complete' && !await confirmCompleteWaitClear()) {
      return
    }

    await runLifecycleAction(type)
  }

  function openSettings() {
    $('#settings-overlay').prop('hidden', false)
    $('#settings-close-button').trigger('focus')
    loadLookupSettings().catch(function (error) {
      setStatus(getErrorMessage(error, 'Could not load lookup settings'), 'error')
      $('#lookup-settings-groups').html('<div class="empty-lookup-settings">Could not load lookup values.</div>')
    })
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
    window.setTimeout(function () {
      const input = $('#new-task-title-input')[0]
      input.focus()
      input.select()
    }, 0)
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

    try {
      closeNewTaskDialog()
      await renderTaskEditor(payload)
      isDirty = true
      window.Editor.markClean()
      setStatus('Unsaved changes', 'dirty')
    } catch (error) {
      $('#new-task-save-button, #new-task-cancel-button').prop('disabled', false)
      throw error
    }
  }

  function bindEvents() {
    $('#new-task-button').on('click', function () {
      allowContextSwitch().then(function (isAllowed) {
        if (isAllowed) {
          openNewTaskDialog()
        }
      })
    })

    $('#settings-button').on('click', openSettings)
    $('#settings-close-button').on('click', closeSettings)
    $('#settings-overlay').on('click', function (event) {
      if (event.target === this) {
        closeSettings()
      }
    })
    $('#settings-overlay').on('click', '.lookup-group-button', function () {
      openLookupList($(this).attr('data-lookup-group')).catch(function (error) {
        setStatus(getErrorMessage(error, 'Could not open lookup values'), 'error')
      })
    })
    $('#lookup-list-close-button, #lookup-list-done-button').on('click', closeLookupList)
    $('#lookup-list-overlay').on('click', function (event) {
      if (event.target === this) {
        closeLookupList()
      }
    })
    $('#lookup-list-new-button').on('click', function () {
      openLookupEdit(null)
    })
    $('#lookup-list-items').on('click', '.lookup-list-edit-button', function () {
      openLookupEdit($(this).attr('data-code'))
    })
    $('#lookup-list-items').on('click', '.lookup-reorder-button', function () {
      reorderLookupItem($(this).attr('data-code'), $(this).attr('data-direction')).catch(function (error) {
        setStatus(getErrorMessage(error, 'Could not save lookup order'), 'error')
      })
    })
    $('#lookup-edit-cancel-button').on('click', closeLookupEdit)
    $('#lookup-edit-overlay').on('click', function (event) {
      if (event.target === this) {
        closeLookupEdit()
      }
    })
    $('#lookup-edit-code, #lookup-edit-name').on('input', function () {
      $(this).removeClass('is-invalid')
      $('#lookup-edit-error').prop('hidden', true).text('')
      updateLookupEditPreview()
    })
    $('#lookup-edit-background-color, #lookup-edit-foreground-color').on('input', updateLookupEditPreview)
    $('#lookup-edit-save-button').on('click', function () {
      saveLookupEdit().catch(function (error) {
        $('#lookup-edit-error').text(getErrorMessage(error, 'Could not save lookup')).prop('hidden', false)
        setStatus(getErrorMessage(error, 'Could not save lookup'), 'error')
      })
    })
    $('#lookup-edit-delete-button').on('click', function () {
      deleteLookupEdit().catch(function (error) {
        $('#lookup-edit-error').text(getErrorMessage(error, 'Could not delete lookup')).prop('hidden', false)
        setStatus(getErrorMessage(error, 'Could not delete lookup'), 'error')
      })
    })
    $('#lookup-edit-overlay').on('keydown', 'input', function (event) {
      if (event.key === 'Enter') {
        event.preventDefault()
        saveLookupEdit().catch(function (error) {
          $('#lookup-edit-error').text(getErrorMessage(error, 'Could not save lookup')).prop('hidden', false)
          setStatus(getErrorMessage(error, 'Could not save lookup'), 'error')
        })
      }
    })
    $('#unsaved-save-button').on('click', function () {
      resolveUnsavedChangesDialog('save')
    })
    $('#unsaved-discard-button').on('click', function () {
      resolveUnsavedChangesDialog('discard')
    })
    $('#unsaved-cancel-button').on('click', function () {
      resolveUnsavedChangesDialog('cancel')
    })
    $('#unsaved-changes-overlay').on('click', function (event) {
      if (event.target === this) {
        resolveUnsavedChangesDialog('cancel')
      }
    })
    $('#complete-wait-clear-button').on('click', function () {
      resolveCompleteWaitDialog('clear')
    })
    $('#complete-wait-cancel-button').on('click', function () {
      resolveCompleteWaitDialog('cancel')
    })
    $('#complete-wait-overlay').on('click', function (event) {
      if (event.target === this) {
        resolveCompleteWaitDialog('cancel')
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
      if (event.key === 'Escape' && !$('#lookup-edit-overlay').prop('hidden')) {
        closeLookupEdit()
        return
      }
      if (event.key === 'Escape' && !$('#lookup-list-overlay').prop('hidden')) {
        closeLookupList()
        return
      }
      if (event.key === 'Escape' && !$('#unsaved-changes-overlay').prop('hidden')) {
        resolveUnsavedChangesDialog('cancel')
      }
      if (event.key === 'Escape' && !$('#complete-wait-overlay').prop('hidden')) {
        resolveCompleteWaitDialog('cancel')
      }
      if (event.key === 'Escape' && !$('#settings-overlay').prop('hidden')) {
        closeSettings()
      }
      if (event.key === 'Escape' && !$('#new-task-overlay').prop('hidden')) {
        closeNewTaskDialog()
      }
    })

    $('#task-list').on('click', '.task-row', function () {
      const taskId = Number($(this).attr('data-task-id'))
      selectTaskWithUnsavedCheck(taskId)
    })

    $('#task-list').on('keydown', function (event) {
      if ($(event.target).is('input, textarea, select')) {
        return
      }

      if (event.key === 'ArrowDown' || event.key === 'ArrowRight') {
        event.preventDefault()
        selectRelativeTask(1)
        return
      }

      if (event.key === 'ArrowUp' || event.key === 'ArrowLeft') {
        event.preventDefault()
        selectRelativeTask(-1)
        return
      }

      if (event.key === 'Home') {
        event.preventDefault()
        selectVisibleTaskByIndex(0)
        return
      }

      if (event.key === 'End') {
        event.preventDefault()
        selectVisibleTaskByIndex(getVisibleTasks().length - 1)
        return
      }

      if (isTextEntryKey(event)) {
        event.preventDefault()
        focusTaskSearchWithKey(event.key)
      }
    })

    $('.view-tab').on('click', function () {
      const targetView = $(this).attr('data-view')
      if (targetView === currentView) {
        return
      }

      allowContextSwitch().then(function (isAllowed) {
        if (!isAllowed) {
          return
        }

        currentView = targetView
        loadTasks({ selectFirst: true }).catch(showFatalError)
      })
    })

    $('#task-search').on('input', renderTaskList)
    $('#attachment-add-button').on('click', function () { $('#attachment-file').trigger('click') })
    $('#attachment-file').on('change', function () {
      addAttachment().catch(function (error) { setStatus(getErrorMessage(error, 'Could not add attachment'), 'error') })
    })
    $('#attachment-list').on('click', '.attachment-download-button', function () {
      downloadAttachment(Number($(this).attr('data-attachment-id'))).catch(function (error) { setStatus(getErrorMessage(error, 'Could not download attachment'), 'error') })
    })
    $('#attachment-list').on('click', '.attachment-delete-button', function () {
      deleteAttachment(Number($(this).attr('data-attachment-id'))).catch(function (error) { setStatus(getErrorMessage(error, 'Could not remove attachment'), 'error') })
    })
    $('#task-form').on('input change', '#task-title, #task-type, #task-priority, #task-deadline, #task-tags, #task-source, #task-source-reference, #task-source-url, #waiting-text', markDirty)
    $('#comment-add-button').on('click', function () {
      addComment().catch(function (error) {
        setStatus(getErrorMessage(error, 'Could not add comment'), 'error')
      })
    })
    $('#comment-text').on('input', function () {
      $(this).removeClass('is-invalid')
    })
    $('#comment-text').on('keydown', function (event) {
      if ((event.ctrlKey || event.metaKey) && event.key === 'Enter') {
        event.preventDefault()
        addComment().catch(function (error) {
          setStatus(getErrorMessage(error, 'Could not add comment'), 'error')
        })
      }
    })
    $('#timeline-list').on('click', '.timeline-delete-comment-button', function () {
      deleteComment(Number($(this).attr('data-comment-id'))).catch(function (error) {
        setStatus(getErrorMessage(error, 'Could not delete comment'), 'error')
      })
    })
    $('#editor-mode').on('change', function () {
      switchEditorMode().catch(function (error) {
        setStatus(getErrorMessage(error, 'Could not switch editor'), 'error')
      })
    })
    $('#editor-height').on('change', function () {
      saveEditorHeightPreferenceFromSettings().catch(function (error) {
        setStatus(getErrorMessage(error, 'Could not save editor height'), 'error')
      })
    })
    $('#editor-height').on('keydown', function (event) {
      if (event.key === 'Enter') {
        event.preventDefault()
        saveEditorHeightPreferenceFromSettings().catch(function (error) {
          setStatus(getErrorMessage(error, 'Could not save editor height'), 'error')
        })
      }
    })
    $('#layout-mode').on('change', switchLayoutMode)
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
      loadLayoutPreference().catch(function (error) {
        setStatus(getErrorMessage(error, 'Could not load layout preference'), 'error')
      })
      lookups = await sendBridgeMessage('task.lookups.get', {})
      renderLookups()
      await loadEditorPreference()
      await loadTasks({ selectFirst: true })
      scheduleEditorPreload()
    } catch (error) {
      showFatalError(error)
    }
  })
})(jQuery)
