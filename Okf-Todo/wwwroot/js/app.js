(function ($) {
  const pendingRequests = new Map()
  const bridgeTimeoutMs = 15000
  const imageBridgeTimeoutMs = 120000
  const viewLabels = {
    active: 'Active',
    urgent: 'Urgent',
    waiting: 'Waiting',
    overdue: 'Overdue',
    completed: 'Completed',
    all: 'All'
  }
  const defaultTaskSortMode = 'ATTENTION'
  const defaultTaskSortDirection = 'ASC'
  const taskSortDirectionCodes = {
    ascending: 'ASC',
    descending: 'DESC'
  }
  const legacyTaskSortModes = {
    STALE_FIRST: 'RECENTLY_UPDATED',
    OLDEST_CREATED: 'NEWEST_CREATED',
    TITLE_DESC: 'TITLE_ASC'
  }
  const taskSortGroups = [
    {
      label: 'Focus',
      options: [
        { code: 'ATTENTION', label: 'Smart priority', description: 'Overdue → urgent → active → waiting → can wait → finished; then earliest deadline.' },
        { code: 'PRIORITY', label: 'Priority', description: 'Your configured priority order, then due date.' },
        { code: 'DUE_DATE', label: 'Due date', description: 'Earliest deadlines first; undated work stays visible.' },
        { code: 'WAITING_LONGEST', label: 'Waiting since', description: 'Sort by how long the task has been waiting.' }
      ]
    },
    {
      label: 'Activity',
      options: [
        { code: 'RECENTLY_UPDATED', label: 'Updated', description: 'Sort by when work last changed.' },
        { code: 'NEWEST_CREATED', label: 'Created', description: 'Sort by when work was captured.' }
      ]
    },
    {
      label: 'Organize',
      options: [
        { code: 'TITLE_ASC', label: 'Title', description: 'Sort tasks alphabetically.' },
        { code: 'TASK_TYPE', label: 'Task type', description: 'Group errors, investigations, requests, and notes.' },
        { code: 'STATUS', label: 'Lifecycle status', description: 'Uses the configured status order (Active → Completed → Cancelled by default); mainly useful in All tasks.' }
      ]
    }
  ]
  const taskSortOptions = taskSortGroups.reduce(function (options, group) {
    return options.concat(group.options)
  }, [])
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
  const lookupSettingsGroupDescriptions = {
    taskTypes: 'Add, edit, or remove task types.',
    taskPriorities: 'Add, edit, or remove priorities.',
    taskStatuses: 'Add, edit, or remove statuses.'
  }
  const preferenceSectionLabels = {
    general: 'General',
    appearance: 'Appearance',
    'task-details': 'Task details',
    'data-values': 'Data & values',
    backup: 'Backup'
  }
  const supportedEditorImageTypes = ['image/png', 'image/jpeg', 'image/gif', 'image/webp']
  const maxEditorImageBytes = 5 * 1024 * 1024
  const taskTransitionRevealDurationMs = 4200
  const wideLayoutMediaQuery = window.matchMedia('(min-width: 901px)')
  const defaultEditorHeight = 360
  const minimumEditorHeight = 200
  const maximumEditorHeight = 1800
  const editorHeightStep = 40
  const defaultTaskListWidth = 410
  const layoutModeCodes = {
    auto: 'AUTO',
    sideBySide: 'SIDE_BY_SIDE',
    stacked: 'STACKED'
  }
  const colorSchemeCodes = {
    light: 'LIGHT',
    dark: 'DARK'
  }
  const colorSchemeStorageKey = 'okf-todo-color-scheme'
  const helpTopics = {
    'okf-layer': '/help/okf-layer.md',
    'mcp-server': '/help/mcp-server.md'
  }

  let lookups = null
  let tasks = []
  let currentTask = null
  let currentView = 'active'
  let taskSortModes = createDefaultTaskSortModes()
  let taskSortDirections = createDefaultTaskSortDirections()
  let isEditorReady = false
  let isDirty = false
  let cleanTaskSnapshot = null
  let preferredBodyFormatCode = 'HTML'
  let preferredMarkdownEditType = 'MARKDOWN'
  let preferredEditorHeight = defaultEditorHeight
  let lookupSettings = null
  let tagSettings = null
  let activeLookupSettingsGroup = 'taskTypes'
  let editingLookupCode = null
  let editingTagId = null
  let layoutPreference = {
    taskListWidth: defaultTaskListWidth,
    taskListHeight: null,
    layoutMode: layoutModeCodes.auto,
    showSourceFields: false,
    showOwner: false,
    showResponsible: false,
    showRelationships: false,
    allowEditingCompletedTasks: false,
    allowEditingCancelledTasks: false,
    taskSortModes: taskSortModes,
    taskSortDirections: taskSortDirections,
    colorScheme: document.documentElement.classList.contains('theme-dark')
      ? colorSchemeCodes.dark
      : colorSchemeCodes.light
  }
  const taskDetailPreferenceByControlId = {
    'show-source-fields': 'showSourceFields',
    'show-owner': 'showOwner',
    'show-responsible': 'showResponsible',
    'show-relationships': 'showRelationships',
    'allow-editing-completed-tasks': 'allowEditingCompletedTasks',
    'allow-editing-cancelled-tasks': 'allowEditingCancelledTasks'
  }
  let layoutPreferenceSaveTimer = null
  let editorHeightPreferenceSaveTimer = null
  let editorHeightDragState = null
  let unsavedChangesDialogResolve = null
  let completeWaitDialogResolve = null
  let confirmationDialogResolve = null
  let dirtyTrackingSuppressions = 0
  let markdownEditTypeSwitchCleanUntil = 0
  let markdownEditTypeSwitchWasClean = false
  let activeHelpTopic = 'okf-layer'
  let activePreferenceSection = 'appearance'
  let taskTransitionReveal = null
  let taskTransitionRevealTimer = null
  const helpDocumentCache = new Map()

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
    const timeoutMs = type === 'database.backup.create'
      ? null
      : type === 'image.create' || type === 'image.get'
        ? imageBridgeTimeoutMs
        : bridgeTimeoutMs

    return new Promise(function (resolve, reject) {
      const timeoutId = timeoutMs === null
        ? null
        : window.setTimeout(function () {
            pendingRequests.delete(messageId)
            reject(new Error(`Timed out waiting for ${type}.`))
          }, timeoutMs)

      pendingRequests.set(messageId, {
        resolve: function (value) {
          if (timeoutId !== null) {
            window.clearTimeout(timeoutId)
          }
          resolve(value)
        },
        reject: function (error) {
          if (timeoutId !== null) {
            window.clearTimeout(timeoutId)
          }
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
        if (timeoutId !== null) {
          window.clearTimeout(timeoutId)
        }
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

  function getLocalTodayDateKey() {
    const today = new Date()
    const month = String(today.getMonth() + 1).padStart(2, '0')
    const day = String(today.getDate()).padStart(2, '0')
    return `${today.getFullYear()}-${month}-${day}`
  }

  function isTaskOverdue(task) {
    const deadline = formatDate(task && task.deadline)
    return task
      && task.taskStatusCode === 'ACTIVE'
      && /^\d{4}-\d{2}-\d{2}$/.test(deadline)
      && deadline < getLocalTodayDateKey()
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

    if (statusCode === 'COMPLETED') {
      return 'completed'
    }

    if (statusCode === 'CANCELLED') {
      return 'all'
    }

    return 'active'
  }

  function selectViewForTask(task) {
    currentView = getViewForTask(task)
  }

  function getLifecycleTransitionPresentation(task) {
    const view = getViewForTask(task)
    const viewLabel = viewLabels[view]
    const statusLabel = task && task.taskStatusName
      ? task.taskStatusName.toString().trim()
      : viewLabel
    const kind = task && task.taskStatusCode === 'CANCELLED'
      ? 'cancelled'
      : task && task.taskStatusCode === 'COMPLETED'
        ? 'completed'
        : 'active'
    const icon = kind === 'cancelled'
      ? '&#xE711;'
      : kind === 'completed'
        ? '&#xE73E;'
        : '&#xE72C;'

    return {
      taskId: Number(task.id),
      view,
      kind,
      icon,
      rowLabel: 'Just moved',
      headerLabel: `${statusLabel.toUpperCase()} · VIEWING IN ${viewLabel.toUpperCase()}`
    }
  }

  function getTaskTransitionReveal(task) {
    if (!taskTransitionReveal || !task || Number(task.id) !== taskTransitionReveal.taskId) {
      return null
    }

    return taskTransitionReveal
  }

  function clearTaskTransitionReveal(shouldRender) {
    if (taskTransitionRevealTimer !== null) {
      window.clearTimeout(taskTransitionRevealTimer)
      taskTransitionRevealTimer = null
    }

    if (!taskTransitionReveal) {
      return
    }

    taskTransitionReveal = null

    if (shouldRender !== false) {
      if (currentTask) {
        renderTaskHeaderAndActions(currentTask)
      }
      renderTaskList()
    }
  }

  function beginTaskTransitionReveal(task) {
    clearTaskTransitionReveal(false)
    taskTransitionReveal = getLifecycleTransitionPresentation(task)
    const revealTaskId = taskTransitionReveal.taskId

    taskTransitionRevealTimer = window.setTimeout(function () {
      if (!taskTransitionReveal || taskTransitionReveal.taskId !== revealTaskId) {
        return
      }

      clearTaskTransitionReveal(true)
    }, taskTransitionRevealDurationMs)

    return taskTransitionReveal
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
    if (!canMutateCurrentTask()) {
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
    if (!isEditorReady
      || !isTaskEditable(currentTask)
      || dirtyTrackingSuppressions > 0
      || Date.now() < markdownEditTypeSwitchCleanUntil) {
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

  function renderTaskSortOptions() {
    return taskSortGroups.map(function (group) {
      const options = group.options.map(function (option) {
        return `<option value="${option.code}">${option.label}</option>`
      }).join('')
      return `<optgroup label="${group.label}">${options}</optgroup>`
    }).join('')
  }

  function renderTaskViewOptions() {
    return Object.keys(viewLabels).map(function (view) {
      return `<option value="${view}">${viewLabels[view]}</option>`
    }).join('')
  }

  function renderTaskViewRail() {
    const viewIcons = {
      active: '&#xE80F;',
      urgent: '&#xE814;',
      waiting: '&#xE823;',
      overdue: '&#xE787;',
      completed: '&#xE73E;',
      all: '&#xEA37;'
    }

    return Object.keys(viewLabels).map(function (view) {
      return `
        <button class="task-view-rail-button" type="button" data-task-view="${view}" aria-label="${viewLabels[view]}" title="${viewLabels[view]}">
          <span class="fluent-icon" aria-hidden="true">${viewIcons[view]}</span>
          <span class="task-view-rail-label">${viewLabels[view]}</span>
        </button>
      `
    }).join('')
  }

  function getTaskSortOption(code) {
    return taskSortOptions.find(function (option) {
      return option.code === code
    }) || taskSortOptions[0]
  }

  function createDefaultTaskSortModes() {
    return Object.keys(viewLabels).reduce(function (modes, view) {
      modes[view] = defaultTaskSortMode
      return modes
    }, {})
  }

  function createDefaultTaskSortDirections() {
    return Object.keys(viewLabels).reduce(function (directions, view) {
      directions[view] = defaultTaskSortDirection
      return directions
    }, {})
  }

  function getCanonicalTaskSortMode(value) {
    const normalized = String(value || defaultTaskSortMode).trim().toUpperCase()
    return legacyTaskSortModes[normalized] || normalized
  }

  function inferLegacyTaskSortDirection(value) {
    const normalized = String(value || defaultTaskSortMode).trim().toUpperCase()
    return normalized === 'RECENTLY_UPDATED'
      || normalized === 'NEWEST_CREATED'
      || normalized === 'TITLE_DESC'
      ? taskSortDirectionCodes.descending
      : taskSortDirectionCodes.ascending
  }

  function normalizeTaskSortModes(values) {
    const normalized = createDefaultTaskSortModes()
    Object.keys(normalized).forEach(function (view) {
      const candidate = values && typeof values[view] === 'string'
        ? getCanonicalTaskSortMode(values[view])
        : defaultTaskSortMode
      normalized[view] = getTaskSortOption(candidate).code
    })
    return normalized
  }

  function normalizeTaskSortDirections(values, rawModes) {
    const normalized = createDefaultTaskSortDirections()
    Object.keys(normalized).forEach(function (view) {
      const candidate = values && typeof values[view] === 'string'
        ? values[view].trim().toUpperCase()
        : inferLegacyTaskSortDirection(rawModes && rawModes[view])
      normalized[view] = candidate === taskSortDirectionCodes.descending
        ? taskSortDirectionCodes.descending
        : taskSortDirectionCodes.ascending
    })
    return normalized
  }

  function getCurrentTaskSortMode() {
    return taskSortModes[currentView] || defaultTaskSortMode
  }

  function getCurrentTaskSortDirection() {
    return taskSortDirections[currentView] || defaultTaskSortDirection
  }

  function syncTaskSortControl() {
    const option = getTaskSortOption(getCurrentTaskSortMode())
    const direction = getCurrentTaskSortDirection()
    const directionLabel = direction === taskSortDirectionCodes.descending
      ? 'Descending'
      : 'Ascending'
    $('#task-sort').val(option.code)
    $('#task-sort-direction')
      .text(direction === taskSortDirectionCodes.descending ? 'Desc' : 'Asc')
      .attr('aria-label', `Sort ${directionLabel.toLowerCase()}`)
      .attr('title', `Sort ${directionLabel.toLowerCase()}`)
    const directionDescription = direction === taskSortDirectionCodes.descending
      ? 'Descending reverses this order.'
      : 'Ascending uses this order.'
    const description = `${option.description} ${directionDescription}`
    $('#task-sort-description').text(description)
    $('.task-sort-field').attr('title', description)
  }

  function renderShell() {
    $('#app').html(`
      <main class="app-shell">
        <header class="app-topbar">
          <div class="app-brand">
            <span class="app-brand-mark fluent-icon" aria-hidden="true">&#xE8A5;</span>
            <div>
              <h1 id="app-title">OKF-Todo</h1>
              <span>Local task system</span>
            </div>
          </div>
          <div class="app-actions" aria-label="Task actions">
            <span id="save-status" class="save-status is-ready" role="status">Ready</span>
            <button id="help-button" class="icon-button setup-button" type="button" aria-label="Help" title="Help">
              <span class="fluent-icon" aria-hidden="true">&#xE897;</span>
              <span>Help</span>
            </button>
            <button id="settings-button" class="icon-button setup-button" type="button" aria-label="Setup" title="Setup">
              <span class="fluent-icon" aria-hidden="true">&#xE713;</span>
              <span>Setup</span>
            </button>
            <button id="new-task-button" type="button">
              <span class="fluent-icon" aria-hidden="true">&#xE710;</span>
              <span>New task</span>
            </button>
            <button id="complete-button" class="secondary-button" type="button" disabled>Complete</button>
            <button id="cancel-button" class="secondary-button danger-button" type="button" disabled>Cancel</button>
            <button id="save-button" type="button" disabled>Save</button>
          </div>
        </header>

        <div class="workspace-shell">
          <nav class="task-view-rail" aria-label="Task views">
            <div class="task-view-rail-heading">
              <span class="fluent-icon" aria-hidden="true">&#xE8FD;</span>
              <span>Views</span>
            </div>
            ${renderTaskViewRail()}
          </nav>

          <aside class="task-sidebar" aria-labelledby="task-list-title">
            <header class="sidebar-header">
              <div>
                <p class="eyebrow">Task queue</p>
                <h2 id="task-list-title">Active</h2>
              </div>
              <span id="task-list-header-count" class="task-list-header-count">0</span>
            </header>

          <div class="task-browse-controls" aria-label="Browse tasks">
            <div class="task-browse-primary">
              <label class="task-view-field task-view-compact" for="task-view">
                <span class="sr-only">Task view</span>
                <select id="task-view" aria-label="Task view">
                  ${renderTaskViewOptions()}
                </select>
              </label>

              <label class="task-search-field" for="task-search">
                <span class="sr-only">Search tasks</span>
                <input id="task-search" class="task-search" type="search" placeholder="Search tasks" autocomplete="off">
                <kbd aria-hidden="true">Ctrl+K</kbd>
              </label>
            </div>

            <div class="task-browse-secondary">
              <div class="task-filter-menu">
                <button id="task-filter-button" class="secondary-button task-filter-button" type="button" aria-expanded="false" aria-controls="task-filter-popover">
                  <span>Tags</span>
                  <span id="task-filter-count-badge" class="task-filter-count-badge" hidden>0</span>
                </button>
                <div id="task-filter-popover" class="task-filter-popover" hidden>
                  <label for="task-tag-filter">Filter by tags</label>
                  <select id="task-tag-filter" multiple aria-label="Filter tasks by tags"></select>
                  <small>Matches any selected tag.</small>
                </div>
              </div>

              <label class="task-quick-filter" for="task-type-filter" title="Filter by task type">
                <span>Type</span>
                <select id="task-type-filter" aria-label="Filter by task type">
                  <option value="">Any</option>
                </select>
              </label>

              <label class="task-quick-filter" for="task-priority-filter" title="Filter by priority">
                <span>Priority</span>
                <select id="task-priority-filter" aria-label="Filter by priority">
                  <option value="">Any</option>
                </select>
              </label>

              <div class="task-sort-field">
                <label for="task-sort">Sort</label>
                <select id="task-sort" aria-describedby="task-sort-description">
                  ${renderTaskSortOptions()}
                </select>
                <button id="task-sort-direction" class="task-sort-direction" type="button" aria-describedby="task-sort-description" aria-label="Sort ascending" title="Sort ascending">Asc</button>
              </div>
              <span id="task-sort-description" class="task-sort-description" aria-live="polite">Overdue → urgent → active → waiting → can wait → finished; then earliest deadline. Ascending uses this order.</span>
            </div>
          </div>

          <div id="task-filter-summary" class="task-filter-summary">
            <div id="task-filter-chips" class="task-filter-chips" aria-label="Active task filters"></div>
            <span id="task-result-count" class="task-result-count" aria-live="polite">0 tasks</span>
            <button id="task-filter-clear" class="task-filter-clear" type="button" hidden>Clear</button>
          </div>

          <div id="task-list" class="task-list" aria-label="Tasks" tabindex="0"></div>
          </aside>

          <div id="layout-resizer" class="layout-resizer" role="separator" aria-label="Resize task list" aria-orientation="vertical" tabindex="0"></div>

          <section class="task-editor-panel" aria-labelledby="task-editor-title">
            <header class="editor-header">
              <p id="task-status-label" class="eyebrow" role="status" aria-live="polite">No task selected</p>
              <h2 id="task-editor-title">Select or create a task</h2>
            </header>

          <form id="task-form" class="task-form">
            <div id="task-read-only-notice" class="task-read-only-notice" role="status" hidden>
              <span class="task-read-only-icon fluent-icon" aria-hidden="true">&#xE72E;</span>
              <span class="task-read-only-copy">
                <strong id="task-read-only-title">Task is read only</strong>
                <span id="task-read-only-message">Reopen this task to make changes.</span>
              </span>
              <button id="task-read-only-reopen-button" class="secondary-button" type="button">Reopen to edit</button>
            </div>

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
            <div
              id="editor-height-resizer"
              class="editor-height-resizer"
              role="separator"
              aria-label="Resize editor height"
              aria-orientation="horizontal"
              aria-valuemin="${minimumEditorHeight}"
              aria-valuemax="${maximumEditorHeight}"
              aria-valuenow="${defaultEditorHeight}"
              aria-valuetext="${defaultEditorHeight} pixels"
              aria-disabled="true"
              tabindex="-1"
              title="Drag vertically to resize the editor"
              hidden>
              <span class="editor-height-resizer-grip" aria-hidden="true"></span>
            </div>

            <div class="ownership-grid" hidden>
              <label class="field-block owner-field" for="task-owner" hidden>
                <span>Owner</span>
                <input id="task-owner" type="text" autocomplete="off" disabled>
              </label>
              <label class="field-block responsible-field" for="task-responsible" hidden>
                <span>Responsible</span>
                <input id="task-responsible" type="text" autocomplete="off" disabled>
              </label>
            </div>

            <div class="source-grid" hidden>
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

            <section class="relationships-section" aria-labelledby="relationships-title" hidden>
              <div class="relationships-header"><h3 id="relationships-title">Relationships</h3></div>
              <div id="relationships-list" class="relationships-list"><span class="empty-relationships">No relationships.</span></div>
              <div class="relationship-add-form">
                <select id="relationship-type" aria-label="Relationship type" disabled></select>
                <select id="relationship-task" aria-label="Related task" disabled></select>
                <button id="relationship-add-button" class="secondary-button" type="button" disabled>Add</button>
              </div>
            </section>

            <section class="checklist-section" aria-labelledby="checklist-title">
              <div class="checklist-header">
                <h3 id="checklist-title">Checklist</h3>
                <span id="checklist-progress"></span>
              </div>
              <div id="checklist-list" class="checklist-list"><span class="empty-checklist">No checklist items.</span></div>
              <div class="checklist-add-form">
                <label class="sr-only" for="checklist-new-text">New checklist item</label>
                <input id="checklist-new-text" type="text" placeholder="Add checklist item" autocomplete="off" disabled>
                <button id="checklist-add-button" class="secondary-button" type="button" disabled>Add</button>
              </div>
            </section>

            <section class="attachments-section" aria-labelledby="attachments-title">
              <div class="attachments-header">
                <h3 id="attachments-title">Attachments</h3>
                <div class="attachment-actions">
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
        </div>

        <div id="help-overlay" class="modal-overlay" hidden>
          <section class="settings-dialog help-dialog" role="dialog" aria-modal="true" aria-labelledby="help-title">
            <header class="settings-header help-header">
              <div>
                <p class="eyebrow">Local integration guides</p>
                <h2 id="help-title">Help</h2>
              </div>
              <button id="help-close-button" class="icon-button" type="button" aria-label="Close help" title="Close">&times;</button>
            </header>

            <div class="help-layout">
              <nav class="help-topic-list" aria-label="Help topics">
                <button class="help-topic-button is-active" type="button" data-help-topic="okf-layer" aria-current="page">
                  <strong>OKF layer</strong>
                  <span>Context graph and command adapter</span>
                </button>
                <button class="help-topic-button" type="button" data-help-topic="mcp-server">
                  <strong>MCP server</strong>
                  <span>Connect an AI client and use tools</span>
                </button>
              </nav>

              <div id="help-content" class="help-content" tabindex="0" aria-live="polite">
                <p class="help-loading">Loading help...</p>
              </div>
            </div>
          </section>
        </div>

        <div id="settings-overlay" class="modal-overlay" hidden>
          <section class="settings-dialog setup-dialog preferences-dialog" role="dialog" aria-modal="true" aria-labelledby="settings-title">
            <header class="settings-header preferences-header">
              <h2 id="settings-title">Preferences</h2>
              <button id="settings-close-button" class="secondary-button preferences-close-button" type="button">Close</button>
            </header>

            <div class="preferences-layout">
              <nav class="preferences-nav" aria-label="Preference sections">
                <button class="preferences-nav-button" type="button" data-preference-section="general">General</button>
                <button class="preferences-nav-button is-active" type="button" data-preference-section="appearance" aria-current="page">Appearance</button>
                <button class="preferences-nav-button" type="button" data-preference-section="task-details">Task details</button>
                <button class="preferences-nav-button" type="button" data-preference-section="data-values">Data &amp; values</button>
                <button class="preferences-nav-button" type="button" data-preference-section="backup">Backup</button>
              </nav>

              <div class="preferences-content" tabindex="0">
                <header class="preferences-content-header">
                  <h3 id="preferences-panel-title">Appearance</h3>
                </header>

                <section class="preferences-page" data-preference-panel="general" aria-labelledby="preferences-editor-title" hidden>
                  <h4 id="preferences-editor-title">Editor</h4>
                  <div class="preference-row">
                    <div class="preference-row-copy">
                      <strong>Editor mode</strong>
                      <span>Choose the default editor for viewing and editing task notes.</span>
                    </div>
                    <div class="preferences-segmented-control" data-preference-select="editor-mode" role="group" aria-label="Editor mode">
                      <button class="preference-choice" type="button" data-value="HTML" aria-pressed="false">HTML</button>
                      <button class="preference-choice" type="button" data-value="MARKDOWN" aria-pressed="false">Markdown</button>
                    </div>
                    <select id="editor-mode" class="sr-only preference-native-control" aria-hidden="true" tabindex="-1" disabled>
                      <option value="HTML">HTML</option>
                      <option value="MARKDOWN">Markdown</option>
                    </select>
                  </div>
                </section>

                <section class="preferences-page" data-preference-panel="appearance" aria-labelledby="preferences-display-title">
                  <h4 id="preferences-display-title">Display</h4>
                  <div class="preference-row">
                    <div class="preference-row-copy">
                      <strong>Color scheme</strong>
                      <span>Choose how OKF-Todo looks.</span>
                    </div>
                    <div class="preferences-segmented-control" data-preference-select="color-scheme" role="group" aria-label="Color scheme">
                      <button class="preference-choice" type="button" data-value="LIGHT" aria-pressed="false">Light</button>
                      <button class="preference-choice" type="button" data-value="DARK" aria-pressed="false">Dark</button>
                    </div>
                    <select id="color-scheme" class="sr-only preference-native-control" aria-hidden="true" tabindex="-1">
                      <option value="LIGHT">Light</option>
                      <option value="DARK">Dark</option>
                    </select>
                  </div>

                  <div class="preference-row">
                    <div class="preference-row-copy">
                      <strong>Task layout</strong>
                      <span>Choose how the task list and task details are arranged.</span>
                    </div>
                    <div class="preferences-segmented-control preferences-layout-control" data-preference-select="layout-mode" role="group" aria-label="Task layout">
                      <button class="preference-choice" type="button" data-value="AUTO" aria-pressed="false">Auto</button>
                      <button class="preference-choice" type="button" data-value="SIDE_BY_SIDE" aria-pressed="false">Side by side</button>
                      <button class="preference-choice" type="button" data-value="STACKED" aria-pressed="false">Stacked</button>
                    </div>
                    <select id="layout-mode" class="sr-only preference-native-control" aria-hidden="true" tabindex="-1">
                      <option value="AUTO">Auto</option>
                      <option value="SIDE_BY_SIDE">Side by side</option>
                      <option value="STACKED">Stacked</option>
                    </select>
                  </div>
                </section>

                <section class="preferences-page" data-preference-panel="task-details" aria-labelledby="preferences-task-details-title" hidden>
                  <h4 id="preferences-task-details-title">Task details</h4>
                  <label class="preference-row preference-toggle-row" for="show-source-fields">
                    <span class="preference-row-copy">
                      <strong>Show source fields</strong>
                      <span>Display source, source reference, and source URL fields.</span>
                    </span>
                    <input id="show-source-fields" type="checkbox" role="switch">
                  </label>

                  <label class="preference-row preference-toggle-row" for="show-owner">
                    <span class="preference-row-copy">
                      <strong>Show owner</strong>
                      <span>Display the optional task owner field below the editor.</span>
                    </span>
                    <input id="show-owner" type="checkbox" role="switch">
                  </label>

                  <label class="preference-row preference-toggle-row" for="show-responsible">
                    <span class="preference-row-copy">
                      <strong>Show responsible</strong>
                      <span>Display the optional responsible person field below the editor.</span>
                    </span>
                    <input id="show-responsible" type="checkbox" role="switch">
                  </label>

                  <label class="preference-row preference-toggle-row" for="show-relationships">
                    <span class="preference-row-copy">
                      <strong>Show relationships</strong>
                      <span>Display related tasks and linked items.</span>
                    </span>
                    <input id="show-relationships" type="checkbox" role="switch">
                  </label>

                  <div class="preferences-subsection-heading">
                    <h4>Finished task editing</h4>
                    <p>Choose whether completed and cancelled work stays directly editable.</p>
                  </div>

                  <label class="preference-row preference-toggle-row" for="allow-editing-completed-tasks">
                    <span class="preference-row-copy">
                      <strong>Allow editing completed tasks</strong>
                      <span>When off, completed tasks are read only until reopened.</span>
                    </span>
                    <input id="allow-editing-completed-tasks" type="checkbox" role="switch">
                  </label>

                  <label class="preference-row preference-toggle-row" for="allow-editing-cancelled-tasks">
                    <span class="preference-row-copy">
                      <strong>Allow editing cancelled tasks</strong>
                      <span>When off, cancelled tasks are read only until reopened.</span>
                    </span>
                    <input id="allow-editing-cancelled-tasks" type="checkbox" role="switch">
                  </label>
                </section>

                <section class="preferences-page preferences-data-group" data-preference-panel="data-values" aria-labelledby="preferences-data-values-title" hidden>
                  <h4 id="preferences-data-values-title">Lookup values</h4>
                  <div id="lookup-settings-groups" class="lookup-group-buttons preferences-management-list" aria-label="Lookup groups"></div>
                  <button id="tag-settings-button" class="lookup-group-button preference-action-row" type="button">
                    <span class="preference-row-copy">
                      <strong>Tags</strong>
                      <span>Rename, merge, or remove task tags.</span>
                    </span>
                    <span class="preference-row-action"><span id="tag-settings-count"></span> Manage</span>
                  </button>
                </section>

                <section class="preferences-page preferences-backup-page" data-preference-panel="backup" aria-labelledby="preferences-backup-title" hidden>
                  <h4 id="preferences-backup-title">Database backup</h4>
                  <p class="preferences-page-intro">Create a portable copy of all OKF-Todo data and attachments.</p>
                  <button id="backup-database-button" class="preference-action-row" type="button">
                    <span class="preference-row-copy">
                      <strong>Create backup</strong>
                      <span>Choose where to save a validated copy of the current database.</span>
                    </span>
                    <span class="preference-row-action">Back up</span>
                  </button>
                  <p id="backup-database-status" class="settings-help preference-status" role="status" aria-live="polite"></p>
                </section>
              </div>
            </div>
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

        <div id="tag-list-overlay" class="modal-overlay" hidden>
          <section class="settings-dialog tag-list-dialog" role="dialog" aria-modal="true" aria-labelledby="tag-list-title">
            <header class="settings-header">
              <h2 id="tag-list-title">Tags</h2>
              <button id="tag-list-close-button" class="icon-button" type="button" aria-label="Close tag list" title="Close">&times;</button>
            </header>

            <div id="tag-list-items" class="tag-list-items"></div>

            <div class="modal-actions">
              <button id="tag-list-done-button" class="secondary-button" type="button">Close</button>
            </div>
          </section>
        </div>

        <div id="tag-edit-overlay" class="modal-overlay" hidden>
          <section class="settings-dialog tag-edit-dialog" role="dialog" aria-modal="true" aria-labelledby="tag-edit-title">
            <header class="settings-header">
              <h2 id="tag-edit-title">Edit tag</h2>
            </header>

            <label class="settings-field" for="tag-edit-value">
              <span>Tag</span>
              <input id="tag-edit-value" type="text" autocomplete="off" required>
            </label>

            <p id="tag-edit-usage" class="settings-help"></p>

            <label class="settings-field" for="tag-merge-target">
              <span>Merge into</span>
              <select id="tag-merge-target"></select>
            </label>

            <p class="settings-help">Merge moves every task to the selected tag and permanently deletes this tag.</p>
            <p id="tag-edit-error" class="form-error" hidden></p>

            <div class="modal-actions">
              <button id="tag-edit-delete-button" class="secondary-button danger-button tag-delete-button" type="button" hidden>Delete</button>
              <button id="tag-edit-cancel-button" class="secondary-button" type="button">Cancel</button>
              <button id="tag-edit-merge-button" class="secondary-button" type="button">Merge and delete</button>
              <button id="tag-edit-save-button" type="button">Save</button>
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

        <div id="confirmation-overlay" class="modal-overlay" hidden>
          <section class="settings-dialog confirmation-dialog" role="dialog" aria-modal="true" aria-labelledby="confirmation-title" aria-describedby="confirmation-message">
            <header class="settings-header">
              <h2 id="confirmation-title">Confirm deletion</h2>
            </header>
            <p id="confirmation-message" class="settings-help"></p>
            <div class="modal-actions">
              <button id="confirmation-cancel-button" class="secondary-button" type="button">Cancel</button>
              <button id="confirmation-confirm-button" class="danger-button" type="button">Delete</button>
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

  function renderTaskListFilterOptions(selector, items) {
    const selectedValue = $(selector).val() || ''
    const options = ['<option value="">Any</option>']

    ;(items || []).forEach(function (item) {
      options.push(`<option value="${encodeAttribute(item.code)}">${encodeText(item.name)}</option>`)
    })

    $(selector).html(options.join('')).val(selectedValue)
  }

  function renderLookups() {
    renderLookupOptions('#task-type', lookups.taskTypes, false)
    renderLookupOptions('#task-priority', lookups.taskPriorities, true)
    renderLookupOptions('#task-source', lookups.taskSources, true)
    renderLookupOptions('#editor-mode', lookups.bodyFormats, false)
    renderTaskListFilterOptions('#task-type-filter', lookups.taskTypes)
    renderTaskListFilterOptions('#task-priority-filter', lookups.taskPriorities)
    renderTagOptions(lookups.tags || [])
    renderTaskTagFilterOptions(lookups.tags || [])
    syncPreferenceControls()
  }

  function resolveConfirmationDialog(isConfirmed) {
    if (!confirmationDialogResolve) return
    const resolve = confirmationDialogResolve
    confirmationDialogResolve = null
    $('#confirmation-overlay').prop('hidden', true)
    resolve(isConfirmed)
  }

  function showConfirmationDialog(title, message, confirmText) {
    $('#confirmation-title').text(title)
    $('#confirmation-message').text(message)
    $('#confirmation-confirm-button').text(confirmText || 'Delete')
    $('#confirmation-overlay').prop('hidden', false)
    window.setTimeout(function () { $('#confirmation-cancel-button').trigger('focus') }, 0)
    return new Promise(function (resolve) {
      confirmationDialogResolve = resolve
    })
  }

  function renderRelationships(items) {
    const rows = Array.isArray(items) ? items : []
    if (!rows.length) {
      $('#relationships-list').html('<span class="empty-relationships">No relationships.</span>')
      setTaskOwnedControlsEnabled(!!currentTask?.id && isTaskEditable(currentTask))
      return
    }
    $('#relationships-list').html(rows.map(function (item) {
      return `<div class="relationship-row" data-relation-id="${item.id}">
        <span class="relationship-name">${encodeText(item.relationName)}</span>
        <button class="relationship-open" type="button" data-task-id="${item.relatedTaskId}">${encodeText(item.relatedTaskTitle)}</button>
        <button class="relationship-delete secondary-button danger-button" type="button" aria-label="Remove relationship to ${encodeAttribute(item.relatedTaskTitle)}">Remove</button>
      </div>`
    }).join(''))
  }

  async function loadRelationships(taskId) {
    if (!taskId) {
      renderRelationships([])
      $('#relationship-type, #relationship-task, #relationship-add-button').prop('disabled', true)
      return
    }
    const result = await Promise.all([
      sendBridgeMessage('task.relation.list', { taskId }),
      sendBridgeMessage('task.relation.options', { taskId })
    ])
    renderRelationships(result[0])
    $('#relationship-type').html(result[1].relationTypes.map(function (type) {
      return `<option value="${encodeAttribute(type.code)}">${encodeText(type.name)}</option>`
    }).join(''))
    $('#relationship-task').html('<option value="">Select task</option>' + result[1].tasks.map(function (task) {
      return `<option value="${task.id}">${encodeText(task.title)}</option>`
    }).join(''))
    setTaskOwnedControlsEnabled(isTaskEditable(currentTask))
  }

  async function addRelationship() {
    const targetTaskId = Number($('#relationship-task').val())
    if (!currentTask || !currentTask.id || !targetTaskId || !canMutateCurrentTask()) return
    const items = await sendBridgeMessage('task.relation.create', {
      taskId: currentTask.id,
      targetTaskId,
      relationTypeCode: $('#relationship-type').val().toString()
    })
    renderRelationships(items)
    $('#relationship-task').val('')
    await loadTimeline(currentTask.id)
    setStatus(isDirty ? 'Unsaved changes' : 'Relationship added', isDirty ? 'dirty' : 'saved')
  }

  async function deleteRelationship(relationId) {
    if (!canMutateCurrentTask()) return
    const items = await sendBridgeMessage('task.relation.delete', { taskId: currentTask.id, relationId })
    renderRelationships(items)
    await loadTimeline(currentTask.id)
    setStatus(isDirty ? 'Unsaved changes' : 'Relationship removed', isDirty ? 'dirty' : 'saved')
  }

  function renderChecklist(items) {
    const checklistItems = Array.isArray(items) ? items : []
    const completedCount = checklistItems.filter(function (item) { return item.isCompleted }).length
    $('#checklist-progress').text(checklistItems.length ? `${completedCount}/${checklistItems.length}` : '')

    if (checklistItems.length === 0) {
      $('#checklist-list').html('<span class="empty-checklist">No checklist items.</span>')
      setTaskOwnedControlsEnabled(!!currentTask?.id && isTaskEditable(currentTask))
      return
    }

    $('#checklist-list').html(checklistItems.map(function (item, index) {
      return `
        <div class="checklist-row${item.isCompleted ? ' is-completed' : ''}" data-checklist-item-id="${item.id}">
          <input class="checklist-completed" type="checkbox" aria-label="Complete ${encodeAttribute(item.text)}"${item.isCompleted ? ' checked' : ''}>
          <input class="checklist-text" type="text" value="${encodeAttribute(item.text)}" aria-label="Checklist item text">
          <button class="checklist-move-up" type="button" title="Move up" aria-label="Move ${encodeAttribute(item.text)} up"${index === 0 ? ' disabled' : ''}>&uarr;</button>
          <button class="checklist-move-down" type="button" title="Move down" aria-label="Move ${encodeAttribute(item.text)} down"${index === checklistItems.length - 1 ? ' disabled' : ''}>&darr;</button>
          <button class="checklist-delete secondary-button danger-button" type="button" aria-label="Delete ${encodeAttribute(item.text)}">Delete</button>
        </div>
      `
    }).join(''))
    setTaskOwnedControlsEnabled(!!currentTask?.id && isTaskEditable(currentTask))
  }

  async function loadChecklist(taskId) {
    if (!taskId) {
      renderChecklist([])
      $('#checklist-new-text, #checklist-add-button').prop('disabled', true)
      return
    }

    renderChecklist(await sendBridgeMessage('task.checklist.list', { taskId }))
    setTaskOwnedControlsEnabled(isTaskEditable(currentTask))
  }

  async function refreshAfterChecklistChange(items, statusText, reloadTimeline) {
    const wasDirty = isDirty
    renderChecklist(items)
    await loadTasks({ keepSelection: true })
    if (reloadTimeline) await loadTimeline(currentTask.id)
    setStatus(wasDirty ? 'Unsaved changes' : statusText, wasDirty ? 'dirty' : 'saved')
  }

  async function addChecklistItem() {
    if (!currentTask || !currentTask.id || !canMutateCurrentTask()) return
    const text = $('#checklist-new-text').val().toString().trim()
    if (!text) {
      $('#checklist-new-text').trigger('focus')
      return
    }

    const items = await sendBridgeMessage('task.checklist.create', { taskId: currentTask.id, text })
    $('#checklist-new-text').val('')
    await refreshAfterChecklistChange(items, 'Checklist item added', true)
  }

  async function updateChecklistItem(checklistItemId, text) {
    if (!canMutateCurrentTask()) return
    const normalizedText = text.trim()
    if (!normalizedText) {
      await loadChecklist(currentTask.id)
      return
    }
    const items = await sendBridgeMessage('task.checklist.update', {
      taskId: currentTask.id,
      checklistItemId,
      text: normalizedText
    })
    await refreshAfterChecklistChange(items, 'Checklist item updated', false)
  }

  async function setChecklistItemCompleted(checklistItemId, isCompleted) {
    if (!canMutateCurrentTask()) return
    const items = await sendBridgeMessage('task.checklist.complete', {
      taskId: currentTask.id,
      checklistItemId,
      isCompleted
    })
    await refreshAfterChecklistChange(items, isCompleted ? 'Checklist item completed' : 'Checklist item reopened', true)
  }

  async function moveChecklistItem(checklistItemId, offset) {
    if (!canMutateCurrentTask()) return
    const ids = $('#checklist-list .checklist-row').map(function () {
      return Number($(this).attr('data-checklist-item-id'))
    }).get()
    const index = ids.indexOf(checklistItemId)
    const targetIndex = index + offset
    if (index < 0 || targetIndex < 0 || targetIndex >= ids.length) return
    ;[ids[index], ids[targetIndex]] = [ids[targetIndex], ids[index]]

    const items = await sendBridgeMessage('task.checklist.reorder', {
      taskId: currentTask.id,
      orderedChecklistItemIds: ids
    })
    await refreshAfterChecklistChange(items, 'Checklist reordered', false)
  }

  async function deleteChecklistItem(checklistItemId) {
    if (!canMutateCurrentTask()) return
    const items = await sendBridgeMessage('task.checklist.delete', {
      taskId: currentTask.id,
      checklistItemId
    })
    await refreshAfterChecklistChange(items, 'Checklist item deleted', false)
  }

  function formatFileSize(size) {
    if (size < 1024) return `${size} B`
    if (size < 1024 * 1024) return `${Math.round(size / 1024)} KB`
    return `${(size / (1024 * 1024)).toFixed(1)} MB`
  }

  function renderAttachments(items) {
    if (!items || items.length === 0) {
      $('#attachment-list').html('<span class="empty-attachments">No attachments.</span>')
      setTaskOwnedControlsEnabled(!!currentTask?.id && isTaskEditable(currentTask))
      return
    }

    $('#attachment-list').html(items.map(function (item) {
      return `<div class="attachment-row">
        <button class="attachment-download-button attachment-name" type="button" data-attachment-id="${item.id}">${encodeText(item.fileName)}</button>
        <span>${formatFileSize(item.fileSize)}</span>
        <button class="attachment-delete-button secondary-button danger-button" type="button" data-attachment-id="${item.id}" aria-label="Remove ${encodeAttribute(item.fileName)}">Remove</button>
      </div>`
    }).join(''))
    setTaskOwnedControlsEnabled(!!currentTask?.id && isTaskEditable(currentTask))
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
    if (!currentTask || !currentTask.id || !canMutateCurrentTask()) return
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
    if (!canMutateCurrentTask()) return
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

  function renderTaskTagFilterOptions(values, selectedValues) {
    const $filter = $('#task-tag-filter')
    const selected = selectedValues || $filter.val() || []
    if ($filter.hasClass('select2-hidden-accessible')) {
      $filter.select2('destroy')
    }

    $filter.html((values || []).map(function (value) {
      const encoded = encodeAttribute(value)
      return `<option value="${encoded}">${encodeText(value)}</option>`
    }).join(''))

    $filter.select2({
      width: '100%',
      placeholder: 'Search tags...',
      closeOnSelect: false,
      dropdownParent: $('.task-filter-popover')
    })

    const available = new Set(values || [])
    $filter.val(selected.filter(function (value) {
      return available.has(value)
    })).trigger('change.select2')
  }

  function renderLookupSettings() {
    $('#lookup-settings-groups').html(Object.keys(lookupSettingsGroups).map(function (group) {
      const count = lookupSettings && lookupSettings[group] ? lookupSettings[group].length : 0
      const countLabel = lookupSettings ? `${count} Manage` : 'Manage'
      return `<button class="lookup-group-button preference-action-row" type="button" data-lookup-group="${group}">
        <span class="preference-row-copy">
          <strong>${lookupSettingsGroups[group]}</strong>
          <span>${lookupSettingsGroupDescriptions[group]}</span>
        </span>
        <span class="preference-row-action">${countLabel}</span>
      </button>`
    }).join(''))
    setTaskOwnedControlsEnabled(!!currentTask?.id && isTaskEditable(currentTask))
  }

  async function loadLookupSettings() {
    $('#lookup-settings-groups').html('<div class="empty-lookup-settings">Loading lookup values.</div>')
    lookupSettings = await sendBridgeMessage('lookup.settings.get', {})
    renderLookupSettings()
  }

  async function backupDatabase() {
    const $button = $('#backup-database-button')
    const $buttonAction = $button.find('.preference-row-action')
    const $backupStatus = $('#backup-database-status')
    $button.prop('disabled', true)
    $buttonAction.text('Backing up...')
    $backupStatus.text('')

    try {
      const result = await sendBridgeMessage('database.backup.create', {})
      if (result.cancelled) {
        $backupStatus.text('Backup cancelled.')
        setStatus('Backup cancelled', 'ready')
        return
      }

      const fileName = String(result.filePath || '').split(/[\\/]/).pop()
      $backupStatus.text(fileName ? `Saved ${fileName}.` : 'Backup saved.')
      setStatus(fileName ? `Backup saved: ${fileName}` : 'Backup saved', 'saved')
    } catch (error) {
      $backupStatus.text(getErrorMessage(error, 'Could not back up database'))
      throw error
    } finally {
      $button.prop('disabled', false)
      $buttonAction.text('Back up')
    }
  }

  function renderTagSettingsCount() {
    $('#tag-settings-count').text(tagSettings ? tagSettings.length : '')
  }

  async function loadTagSettings() {
    $('#tag-settings-count').text('...')
    tagSettings = await sendBridgeMessage('tag.settings.list', {})
    renderTagSettingsCount()
  }

  function findTagSetting(tagId) {
    return (tagSettings || []).find(function (tag) {
      return tag.id === tagId
    }) || null
  }

  function renderTagList() {
    const items = tagSettings || []
    if (items.length === 0) {
      $('#tag-list-items').html('<div class="empty-lookup-settings">No tags.</div>')
      return
    }

    $('#tag-list-items').html(items.map(function (tag) {
      const usageText = tag.usageCount === 1 ? '1 task' : `${tag.usageCount} tasks`
      return `
        <article class="tag-list-row">
          <span class="tag-list-value">${encodeText(tag.value)}</span>
          <span class="lookup-state-pill">${usageText}</span>
          <button class="tag-list-edit-button secondary-button" type="button" data-tag-id="${tag.id}">Edit</button>
        </article>
      `
    }).join(''))
  }

  async function openTagList() {
    if (!tagSettings) {
      await loadTagSettings()
    }

    renderTagList()
    $('#tag-list-overlay').prop('hidden', false)
    $('#tag-list-close-button').trigger('focus')
  }

  function closeTagList() {
    $('#tag-list-overlay').prop('hidden', true)
    $('#tag-settings-button').trigger('focus')
  }

  function openTagEdit(tagId) {
    editingTagId = tagId
    const tag = findTagSetting(tagId)
    if (!tag) {
      return
    }

    const mergeTargets = (tagSettings || []).filter(function (candidate) {
      return candidate.id !== tag.id
    })
    const usageText = tag.usageCount === 1
      ? 'Used by 1 task.'
      : `Used by ${tag.usageCount} tasks.`

    $('#tag-edit-value').val(tag.value).removeClass('is-invalid')
    $('#tag-edit-usage').text(usageText)
    $('#tag-merge-target').html(
      '<option value="">Select tag</option>'
      + mergeTargets.map(function (candidate) {
        return `<option value="${candidate.id}">${encodeText(candidate.value)}</option>`
      }).join(''))
    $('#tag-edit-delete-button').prop('hidden', tag.usageCount !== 0).prop('disabled', false)
    $('#tag-edit-merge-button').prop('disabled', mergeTargets.length === 0)
    $('#tag-edit-save-button, #tag-edit-cancel-button').prop('disabled', false)
    $('#tag-edit-error').text('').prop('hidden', true)
    $('#tag-edit-overlay').prop('hidden', false)

    window.setTimeout(function () {
      const input = $('#tag-edit-value')[0]
      input.focus()
      input.select()
    }, 0)
  }

  function closeTagEdit() {
    $('#tag-edit-overlay').prop('hidden', true)
    editingTagId = null
    $('#tag-list-close-button').trigger('focus')
  }

  function replaceTagValue(values, oldValue, newValue) {
    const normalizedOld = String(oldValue || '').toLocaleLowerCase()
    const replaced = (values || []).map(function (value) {
      return String(value).toLocaleLowerCase() === normalizedOld ? newValue : value
    }).filter(Boolean)

    return replaced.filter(function (value, index) {
      return replaced.findIndex(function (candidate) {
        return String(candidate).toLocaleLowerCase() === String(value).toLocaleLowerCase()
      }) === index
    }).sort(function (left, right) {
      return String(left).localeCompare(String(right), undefined, { sensitivity: 'base' })
    })
  }

  async function refreshTagDependentUi(oldValue, newValue) {
    const wasDirty = isDirty
    const selectedTags = currentTask
      ? ($('#task-tags').val() || currentTask.tags || [])
      : []
    const currentTaskWasAffected = selectedTags.some(function (value) {
      return String(value).toLocaleLowerCase() === String(oldValue || '').toLocaleLowerCase()
    })
    const nextSelectedTags = replaceTagValue(selectedTags, oldValue, newValue)
    const nextFilterTags = replaceTagValue($('#task-tag-filter').val() || [], oldValue, newValue)

    lookups = await sendBridgeMessage('task.lookups.get', {})
    renderTagOptions(lookups.tags || [])
    renderTaskTagFilterOptions(lookups.tags || [], nextFilterTags)

    if (currentTask) {
      currentTask.tags = nextSelectedTags
      setTaskTags(nextSelectedTags)
      if (cleanTaskSnapshot) {
        cleanTaskSnapshot.tags = replaceTagValue(cleanTaskSnapshot.tags, oldValue, newValue)
      }
      isDirty = wasDirty
    }

    await loadTasks({ keepSelection: true })
    if (currentTaskWasAffected && currentTask && currentTask.id) {
      await loadTimeline(currentTask.id)
    }
  }

  async function saveTagEdit() {
    const tag = findTagSetting(editingTagId)
    const value = $('#tag-edit-value').val().toString().trim()
    if (!tag || !value) {
      $('#tag-edit-value').addClass('is-invalid').trigger('focus')
      $('#tag-edit-error').text('Tag value is required.').prop('hidden', false)
      return
    }

    $('#tag-edit-save-button, #tag-edit-delete-button, #tag-edit-merge-button, #tag-edit-cancel-button').prop('disabled', true)
    try {
      tagSettings = await sendBridgeMessage('tag.settings.rename', {
        tagId: tag.id,
        value
      })
      await refreshTagDependentUi(tag.value, value)
      renderTagSettingsCount()
      renderTagList()
      closeTagEdit()
      setStatus('Tag renamed', 'saved')
    } finally {
      $('#tag-edit-save-button, #tag-edit-delete-button, #tag-edit-merge-button, #tag-edit-cancel-button').prop('disabled', false)
    }
  }

  async function deleteTagEdit() {
    const tag = findTagSetting(editingTagId)
    if (!tag || tag.usageCount !== 0) {
      return
    }

    if (!await showConfirmationDialog('Delete tag?', `Delete “${tag.value}”? This cannot be undone.`, 'Delete tag')) {
      return
    }

    tagSettings = await sendBridgeMessage('tag.settings.delete', { tagId: tag.id })
    await refreshTagDependentUi(tag.value, null)
    renderTagSettingsCount()
    renderTagList()
    closeTagEdit()
    setStatus('Tag deleted', 'saved')
  }

  async function mergeTagEdit() {
    const source = findTagSetting(editingTagId)
    const targetTagId = Number($('#tag-merge-target').val())
    const target = findTagSetting(targetTagId)
    if (!source || !target) {
      $('#tag-merge-target').addClass('is-invalid').trigger('focus')
      $('#tag-edit-error').text('Select a target tag.').prop('hidden', false)
      return
    }

    const message = `Move ${source.usageCount} task association${source.usageCount === 1 ? '' : 's'} from “${source.value}” to “${target.value}” and delete “${source.value}”?`
    if (!await showConfirmationDialog('Merge tags?', message, 'Merge tags')) {
      return
    }

    $('#tag-edit-save-button, #tag-edit-delete-button, #tag-edit-merge-button, #tag-edit-cancel-button').prop('disabled', true)
    try {
      tagSettings = await sendBridgeMessage('tag.settings.merge', {
        sourceTagId: source.id,
        targetTagId: target.id
      })
      await refreshTagDependentUi(source.value, target.value)
      renderTagSettingsCount()
      renderTagList()
      closeTagEdit()
      setStatus('Tags merged', 'saved')
    } finally {
      $('#tag-edit-save-button, #tag-edit-delete-button, #tag-edit-merge-button, #tag-edit-cancel-button').prop('disabled', false)
    }
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
    const isReadOnlyGroup = activeLookupSettingsGroup === 'bodyFormats' || activeLookupSettingsGroup === 'taskLogTypes'
    $('#lookup-list-new-button').text(`New ${lookupSettingsGroupNouns[activeLookupSettingsGroup]}`).prop('hidden', isReadOnlyGroup)

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
      const readOnlyText = item.isReadOnly ? '<span class="lookup-state-pill">Read only</span>' : ''
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
            ${readOnlyText}
            ${usedText}
          </div>
          <button class="lookup-list-edit-button secondary-button" type="button" data-code="${encodedCode}">${item.isReadOnly ? 'View' : 'Edit'}</button>
          <div class="lookup-list-order" aria-label="Order ${encodedName}">
            <button class="lookup-reorder-button" type="button" data-code="${encodedCode}" data-direction="up" title="Move up" aria-label="Move ${encodedName} up"${item.isReadOnly || index === 0 ? ' disabled' : ''}>&uarr;</button>
            <button class="lookup-reorder-button" type="button" data-code="${encodedCode}" data-direction="down" title="Move down" aria-label="Move ${encodedName} down"${item.isReadOnly || index === items.length - 1 ? ' disabled' : ''}>&darr;</button>
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
    const isReadOnly = !!(item && item.isReadOnly)

    $('#lookup-edit-title').text(editingLookupCode
      ? `Edit ${lookupSettingsGroupNouns[activeLookupSettingsGroup]}`
      : `New ${lookupSettingsGroupNouns[activeLookupSettingsGroup]}`)
    $('#lookup-edit-code')
      .val(item ? item.code : '')
      .prop('disabled', !!item)
      .removeClass('is-invalid')
    $('#lookup-edit-name').val(item ? item.name : '').prop('disabled', isReadOnly).removeClass('is-invalid')
    $('#lookup-edit-description').val(item && item.description ? item.description : '').prop('disabled', isReadOnly)
    $('#lookup-edit-is-active')
      .prop('checked', item ? item.isActive : true)
      .prop('disabled', isReadOnly || !!(item && activeLookupSettingsGroup === 'taskStatuses' && item.isSystem))
    $('#lookup-edit-is-selected').prop('checked', item ? item.isSelected : false).prop('disabled', isReadOnly)
    $('#lookup-edit-background-color').val(getColorInputValue(item && item.backgroundColor, '#6b7280')).prop('disabled', isReadOnly)
    $('#lookup-edit-foreground-color').val(getColorInputValue(item && item.foregroundColor, '#ffffff')).prop('disabled', isReadOnly)
    $('#lookup-edit-error').prop('hidden', true).text('')
    $('#lookup-edit-delete-button')
      .prop('hidden', !(item && item.canDelete))
      .prop('disabled', false)
    $('#lookup-edit-save-button').prop('hidden', isReadOnly).prop('disabled', false)
    $('#lookup-edit-cancel-button').prop('disabled', false).text(isReadOnly ? 'Close' : 'Cancel')
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

  function syncEditorHeightControls() {
    $('#editor-height-resizer')
      .attr('aria-valuenow', preferredEditorHeight)
      .attr('aria-valuetext', `${preferredEditorHeight} pixels`)
  }

  function setEditorHeightControlEnabled(enabled) {
    if (!enabled && editorHeightDragState) {
      cancelEditorHeightResize()
    }

    $('#editor-height-resizer')
      .prop('hidden', !enabled)
      .attr('aria-disabled', String(!enabled))
      .attr('tabindex', enabled ? '0' : '-1')
  }

  async function loadEditorPreference() {
    const preference = await sendBridgeMessage('editor.preference.get', {})
    preferredBodyFormatCode = getSupportedBodyFormatCode(preference.bodyFormatCode)
    preferredMarkdownEditType = getSupportedMarkdownEditType(preference.markdownEditType)
    preferredEditorHeight = getSupportedEditorHeight(preference.editorHeight)
    $('#editor-mode').val(preferredBodyFormatCode)
    $('#editor-mode').prop('disabled', false)
    syncEditorHeightControls()
    syncPreferenceControls()
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

  function applyEditorHeightPreference() {
    syncEditorHeightControls()

    if (!window.Editor || typeof window.Editor.setHeight !== 'function' || !isEditorReady) {
      return
    }

    window.Editor.setHeight(preferredEditorHeight)
  }

  function previewEditorHeightPreference(value) {
    preferredEditorHeight = getSupportedEditorHeight(value)
    applyEditorHeightPreference()
  }

  function saveEditorHeightPreferenceFromControl() {
    if (editorHeightPreferenceSaveTimer) {
      window.clearTimeout(editorHeightPreferenceSaveTimer)
      editorHeightPreferenceSaveTimer = null
    }

    return saveEditorPreference(preferredBodyFormatCode, preferredMarkdownEditType, preferredEditorHeight)
      .then(function () {
        syncEditorHeightControls()
        setStatus('Editor height preference saved', 'saved')
      })
  }

  function queueEditorHeightPreferenceSave() {
    if (editorHeightPreferenceSaveTimer) {
      window.clearTimeout(editorHeightPreferenceSaveTimer)
    }

    editorHeightPreferenceSaveTimer = window.setTimeout(function () {
      editorHeightPreferenceSaveTimer = null
      saveEditorHeightPreferenceFromControl().catch(function (error) {
        setStatus(getErrorMessage(error, 'Could not save editor height'), 'error')
      })
    }, 400)
  }

  function beginEditorHeightResize(event) {
    if (event.button !== 0 || $('#editor-height-resizer').attr('aria-disabled') === 'true') {
      return
    }

    if (editorHeightPreferenceSaveTimer) {
      window.clearTimeout(editorHeightPreferenceSaveTimer)
      editorHeightPreferenceSaveTimer = null
    }

    editorHeightDragState = {
      pointerId: event.pointerId,
      startY: event.clientY,
      startHeight: preferredEditorHeight
    }

    event.currentTarget.setPointerCapture(event.pointerId)
    $(event.currentTarget).addClass('is-dragging')
    $('body').addClass('is-resizing-editor')
    event.preventDefault()
  }

  function updateEditorHeightResize(event) {
    if (!editorHeightDragState || editorHeightDragState.pointerId !== event.pointerId) {
      return
    }

    const delta = Math.round(event.clientY - editorHeightDragState.startY)
    previewEditorHeightPreference(editorHeightDragState.startHeight + delta)
    event.preventDefault()
  }

  function finishEditorHeightResize(event) {
    if (!editorHeightDragState || editorHeightDragState.pointerId !== event.pointerId) {
      return
    }

    const startHeight = editorHeightDragState.startHeight
    editorHeightDragState = null
    $(event.currentTarget).removeClass('is-dragging')
    $('body').removeClass('is-resizing-editor')

    if (event.currentTarget.hasPointerCapture(event.pointerId)) {
      event.currentTarget.releasePointerCapture(event.pointerId)
    }

    if (preferredEditorHeight !== startHeight) {
      saveEditorHeightPreferenceFromControl().catch(function (error) {
        setStatus(getErrorMessage(error, 'Could not save editor height'), 'error')
      })
    }
  }

  function cancelEditorHeightResize() {
    if (!editorHeightDragState) {
      return
    }

    const dragState = editorHeightDragState
    const resizer = document.getElementById('editor-height-resizer')
    editorHeightDragState = null
    $('#editor-height-resizer').removeClass('is-dragging')
    $('body').removeClass('is-resizing-editor')

    if (resizer && resizer.hasPointerCapture(dragState.pointerId)) {
      resizer.releasePointerCapture(dragState.pointerId)
    }

    previewEditorHeightPreference(dragState.startHeight)
  }

  function resizeEditorFromKeyboard(event) {
    let nextHeight = null

    switch (event.key) {
      case 'ArrowUp':
      case 'ArrowLeft':
        nextHeight = preferredEditorHeight - editorHeightStep
        break
      case 'ArrowDown':
      case 'ArrowRight':
        nextHeight = preferredEditorHeight + editorHeightStep
        break
      case 'PageUp':
        nextHeight = preferredEditorHeight - (editorHeightStep * 4)
        break
      case 'PageDown':
        nextHeight = preferredEditorHeight + (editorHeightStep * 4)
        break
      case 'Home':
        nextHeight = minimumEditorHeight
        break
      case 'End':
        nextHeight = maximumEditorHeight
        break
      default:
        return
    }

    event.preventDefault()
    previewEditorHeightPreference(nextHeight)
    queueEditorHeightPreferenceSave()
  }

  function renderEmptyEditor() {
    currentTask = null
    isDirty = false
    isEditorReady = false
    setEditorHeightControlEnabled(false)
    cleanTaskSnapshot = null
    clearValidationState()

    if (window.Editor && typeof window.Editor.destroy === 'function') {
      window.Editor.destroy()
    }

    $('#task-status-label')
      .text('No task selected')
      .attr('title', 'No task selected')
      .removeClass('is-waiting-context')
    $('#task-editor-title').text('Select or create a task')
    $('#task-read-only-notice').prop('hidden', true)
    $('#task-form').removeClass('is-task-read-only').attr('aria-readonly', null)
    $('#task-title').val('')
    $('#task-type').val('')
    $('#task-priority').val('')
    $('#task-deadline').val('')
    $('#task-source').val('')
    $('#task-source-reference').val('')
    $('#task-source-url').val('')
    $('#task-owner').val('')
    $('#task-responsible').val('')
    setTaskTags([])
    $('#editor-mode').val(getSupportedBodyFormatCode(preferredBodyFormatCode))
    $('#waiting-text').val('')
    $('#task-form input, #task-form select').prop('disabled', true)
    $('#complete-button, #cancel-button, #save-button').prop('disabled', true)
    $('#editor-mode').prop('disabled', !lookups)
    $('#editor-host').html('<div class="empty-editor">Select a task to edit the body.</div>')
    $('#comment-text').val('').removeClass('is-invalid')
    setCommentControlsEnabled(false)
    renderRelationships([])
    $('#relationship-type, #relationship-task, #relationship-add-button').prop('disabled', true)
    renderChecklist([])
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

  function normalizeColorScheme(colorScheme) {
    return String(colorScheme || '').trim().toUpperCase() === colorSchemeCodes.dark
      ? colorSchemeCodes.dark
      : colorSchemeCodes.light
  }

  function applyColorScheme(colorScheme) {
    const normalizedColorScheme = normalizeColorScheme(colorScheme)
    const isDark = normalizedColorScheme === colorSchemeCodes.dark
    const darkThemeStylesheet = document.getElementById('dark-theme-stylesheet')

    layoutPreference.colorScheme = normalizedColorScheme
    document.documentElement.classList.toggle('theme-dark', isDark)
    document.documentElement.style.colorScheme = isDark ? 'dark' : 'light'
    $('#color-scheme').val(normalizedColorScheme)

    if (darkThemeStylesheet) {
      darkThemeStylesheet.disabled = !isDark
    }

    try {
      window.localStorage.setItem(colorSchemeStorageKey, normalizedColorScheme)
    } catch {
      // The persisted bridge preference remains authoritative.
    }

    if (window.Editor && typeof window.Editor.setColorScheme === 'function') {
      window.Editor.setColorScheme(normalizedColorScheme)
    }

    syncPreferenceControls()
  }

  function saveLayoutPreference() {
    if (layoutPreferenceSaveTimer) {
      window.clearTimeout(layoutPreferenceSaveTimer)
      layoutPreferenceSaveTimer = null
    }

    return sendBridgeMessage('layout.preference.save', {
      taskListWidth: layoutPreference.taskListWidth,
      taskListHeight: layoutPreference.taskListHeight,
      layoutMode: layoutPreference.layoutMode,
      showSourceFields: layoutPreference.showSourceFields,
      showOwner: layoutPreference.showOwner,
      showResponsible: layoutPreference.showResponsible,
      showRelationships: layoutPreference.showRelationships,
      allowEditingCompletedTasks: layoutPreference.allowEditingCompletedTasks,
      allowEditingCancelledTasks: layoutPreference.allowEditingCancelledTasks,
      colorScheme: layoutPreference.colorScheme,
      taskSortModes: taskSortModes,
      taskSortDirections: taskSortDirections
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
    layoutPreference.taskListHeight = preference.taskListHeight || Math.round(window.innerHeight * 0.34)
    layoutPreference.layoutMode = normalizeLayoutMode(preference.layoutMode)
    layoutPreference.showSourceFields = preference.showSourceFields === true
    layoutPreference.showOwner = preference.showOwner === true
    layoutPreference.showResponsible = preference.showResponsible === true
    layoutPreference.showRelationships = preference.showRelationships === true
    layoutPreference.allowEditingCompletedTasks = preference.allowEditingCompletedTasks === true
    layoutPreference.allowEditingCancelledTasks = preference.allowEditingCancelledTasks === true
    taskSortModes = normalizeTaskSortModes(preference.taskSortModes)
    layoutPreference.taskSortModes = taskSortModes
    taskSortDirections = normalizeTaskSortDirections(preference.taskSortDirections, preference.taskSortModes)
    layoutPreference.taskSortDirections = taskSortDirections
    applyColorScheme(preference.colorScheme)
    applyStoredLayoutSplit(false)
    syncTaskSortControl()
    if (tasks.length > 0) {
      renderTaskList()
    }
  }

  function clampSplitValue(value, minimum, maximum) {
    return Math.max(minimum, Math.min(maximum, value))
  }

  function getLayoutBounds() {
    const workspace = $('.workspace-shell')[0]
    const rect = workspace.getBoundingClientRect()
    const topbar = $('.app-topbar')[0]
    const topbarHeight = topbar ? topbar.getBoundingClientRect().height : 64

    return {
      width: rect.width > 320 ? rect.width : window.innerWidth,
      height: rect.height > 240
        ? rect.height
        : Math.max(320, window.innerHeight - topbarHeight)
    }
  }

  function setTaskListWidth(width, shouldSave) {
    const bounds = getLayoutBounds()
    const rail = $('.task-view-rail')[0]
    const railWidth = rail && getComputedStyle(rail).display !== 'none'
      ? rail.getBoundingClientRect().width
      : 0
    const maximumWidth = Math.max(300, bounds.width - railWidth - 520)
    const clampedWidth = clampSplitValue(width, 300, maximumWidth)
    document.documentElement.style.setProperty('--task-list-width', `${clampedWidth}px`)
    layoutPreference.taskListWidth = Math.round(clampedWidth)

    if (shouldSave) {
      scheduleLayoutPreferenceSave()
    }
  }

  function setTaskListHeight(height, shouldSave) {
    const bounds = getLayoutBounds()
    const maximumHeight = Math.max(
      230,
      Math.min(Math.round(bounds.height * 0.44), bounds.height - 420))
    const clampedHeight = clampSplitValue(height, 220, maximumHeight)
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
    syncPreferenceControls()
  }

  function applyTaskSectionVisibility() {
    const showOwnershipFields = layoutPreference.showOwner || layoutPreference.showResponsible
    $('.source-grid').prop('hidden', !layoutPreference.showSourceFields)
    $('.ownership-grid')
      .prop('hidden', !showOwnershipFields)
      .toggleClass(
        'is-single-field',
        showOwnershipFields && !(layoutPreference.showOwner && layoutPreference.showResponsible))
    $('.owner-field').prop('hidden', !layoutPreference.showOwner)
    $('.responsible-field').prop('hidden', !layoutPreference.showResponsible)
    $('.relationships-section').prop('hidden', !layoutPreference.showRelationships)
    $('#show-source-fields').prop('checked', layoutPreference.showSourceFields)
    $('#show-owner').prop('checked', layoutPreference.showOwner)
    $('#show-responsible').prop('checked', layoutPreference.showResponsible)
    $('#show-relationships').prop('checked', layoutPreference.showRelationships)
    $('#allow-editing-completed-tasks').prop('checked', layoutPreference.allowEditingCompletedTasks)
    $('#allow-editing-cancelled-tasks').prop('checked', layoutPreference.allowEditingCancelledTasks)
  }

  function applyStoredLayoutSplit(shouldSave) {
    applyLayoutMode()
    applyTaskSectionVisibility()
    setTaskListWidth(getLayoutPreferenceValue('taskListWidth', defaultTaskListWidth), shouldSave)
    setTaskListHeight(getLayoutPreferenceValue('taskListHeight', Math.round(window.innerHeight * 0.34)), shouldSave)
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

  function saveTaskDetailPreference(event) {
    const preferenceName = taskDetailPreferenceByControlId[event.currentTarget.id]
    if (!preferenceName) {
      return
    }

    layoutPreference[preferenceName] = $(event.currentTarget).prop('checked')
    applyTaskSectionVisibility()
    applyCurrentTaskEditability()
    saveLayoutPreference().then(function (wasSaved) {
      if (wasSaved) {
        const statusMessage = preferenceName.startsWith('show')
          ? 'Display preference saved'
          : 'Editing preference saved'
        setStatus(statusMessage, 'saved')
      }
    })
  }

  function switchColorScheme() {
    applyColorScheme($('#color-scheme').val())
    saveLayoutPreference().then(function (wasSaved) {
      if (wasSaved) {
        setStatus('Color scheme saved', 'saved')
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
      const sidebar = $('.task-sidebar')[0]
      const sidebarRect = sidebar.getBoundingClientRect()
      $resizer.addClass('is-dragging')

      $(document).on('pointermove.layoutResizer', function (moveEvent) {
        if (isWideLayout()) {
          setTaskListWidth(moveEvent.clientX - sidebarRect.left, true)
        } else {
          setTaskListHeight(moveEvent.clientY - sidebarRect.top, true)
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
      const currentHeight = getLayoutPreferenceValue('taskListHeight', Math.round(window.innerHeight * 0.34))

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

  function getSelectedTaskTagFilters() {
    return getSelectedTaskTagFilterValues().map(function (tag) {
      return String(tag).toLocaleLowerCase()
    })
  }

  function getSelectedTaskTagFilterValues() {
    return ($('#task-tag-filter').val() || []).map(function (tag) {
      return String(tag)
    })
  }

  function getTaskListFilterValue(selector) {
    return ($(selector).val() || '').toString().trim().toUpperCase()
  }

  function getTaskListFilterName(items, code) {
    const match = (items || []).find(function (item) {
      return item.code === code
    })
    return match ? match.name : code
  }

  function setTaskFilterPopoverOpen(isOpen, shouldFocus) {
    const $popover = $('#task-filter-popover')
    const $button = $('#task-filter-button')
    $popover.prop('hidden', !isOpen)
    $button.attr('aria-expanded', isOpen ? 'true' : 'false')

    if (!isOpen) {
      const $filter = $('#task-tag-filter')
      if ($filter.hasClass('select2-hidden-accessible')) {
        $filter.select2('close')
      }
      return
    }

    if (shouldFocus) {
      window.setTimeout(function () {
        const $filter = $('#task-tag-filter')
        if ($filter.hasClass('select2-hidden-accessible')) {
          $filter.select2('open')
        } else {
          $filter.trigger('focus')
        }
      }, 0)
    }
  }

  function renderTaskFilterSummary(countLabel) {
    const selectedTags = getSelectedTaskTagFilterValues()
    const taskTypeCode = getTaskListFilterValue('#task-type-filter')
    const taskPriorityCode = getTaskListFilterValue('#task-priority-filter')
    const query = getTaskSearchQuery()
    const hasFilters = query.length > 0
      || selectedTags.length > 0
      || taskTypeCode.length > 0
      || taskPriorityCode.length > 0

    const chips = selectedTags.map(function (tag) {
      return `<button class="task-filter-chip" type="button" data-tag="${encodeAttribute(tag)}" aria-label="Remove tag filter: ${encodeAttribute(tag)}" title="Remove tag filter">Tag: ${encodeText(tag)}</button>`
    })
    if (taskTypeCode) {
      const taskTypeName = getTaskListFilterName(lookups.taskTypes, taskTypeCode)
      chips.push(`<button class="task-filter-chip" type="button" data-filter="task-type" aria-label="Remove task type filter: ${encodeAttribute(taskTypeName)}" title="Remove task type filter">Type: ${encodeText(taskTypeName)}</button>`)
    }
    if (taskPriorityCode) {
      const taskPriorityName = getTaskListFilterName(lookups.taskPriorities, taskPriorityCode)
      chips.push(`<button class="task-filter-chip" type="button" data-filter="task-priority" aria-label="Remove priority filter: ${encodeAttribute(taskPriorityName)}" title="Remove priority filter">Priority: ${encodeText(taskPriorityName)}</button>`)
    }

    $('#task-filter-chips').html(chips.join(''))
    $('#task-result-count').text(countLabel)
    $('#task-filter-clear').prop('hidden', !hasFilters)
    $('#task-filter-count-badge')
      .text(selectedTags.length)
      .prop('hidden', selectedTags.length === 0)
    $('#task-filter-button').attr(
      'aria-label',
      selectedTags.length === 0
        ? 'Filter tasks by tags'
        : `Filter tasks by tags, ${selectedTags.length} selected`)
  }

  function clearTaskFilters() {
    $('#task-search').val('')
    $('#task-tag-filter').val([]).trigger('change.select2')
    $('#task-type-filter, #task-priority-filter').val('')
    setTaskFilterPopoverOpen(false, false)
    renderTaskList()
  }

  function compareTaskText(left, right) {
    return String(left || '').localeCompare(String(right || ''), undefined, {
      sensitivity: 'base',
      numeric: true
    })
  }

  function parseTaskDate(value) {
    if (!value) {
      return null
    }

    const parsed = Date.parse(value)
    return Number.isFinite(parsed) ? parsed : null
  }

  function compareNullableValues(left, right, direction) {
    if (left === null && right === null) return 0
    if (left === null) return 1
    if (right === null) return -1
    return (left - right) * direction
  }

  function compareTaskTieBreakers(left, right) {
    return compareTaskText(left.title, right.title) || left.id - right.id
  }

  function sortTasksForCurrentView(filteredTasks) {
    const mode = getCurrentTaskSortMode()
    const direction = getCurrentTaskSortDirection() === taskSortDirectionCodes.descending ? -1 : 1
    if (mode === defaultTaskSortMode) {
      const orderedTasks = filteredTasks.slice()
      return direction === -1 ? orderedTasks.reverse() : orderedTasks
    }

    return filteredTasks.slice().sort(function (left, right) {
      let comparison = 0

      switch (mode) {
        case 'PRIORITY':
          comparison = compareNullableValues(
            Number.isFinite(left.taskPrioritySortOrder) ? left.taskPrioritySortOrder : null,
            Number.isFinite(right.taskPrioritySortOrder) ? right.taskPrioritySortOrder : null,
            direction)
          comparison = comparison || compareNullableValues(
            parseTaskDate(left.deadline),
            parseTaskDate(right.deadline),
            direction)
          break
        case 'DUE_DATE':
          comparison = compareNullableValues(
            parseTaskDate(left.deadline),
            parseTaskDate(right.deadline),
            direction)
          comparison = comparison || compareNullableValues(
            Number.isFinite(left.taskPrioritySortOrder) ? left.taskPrioritySortOrder : null,
            Number.isFinite(right.taskPrioritySortOrder) ? right.taskPrioritySortOrder : null,
            direction)
          break
        case 'WAITING_LONGEST':
          comparison = compareNullableValues(
            parseTaskDate(left.waitingSince),
            parseTaskDate(right.waitingSince),
            direction)
          break
        case 'RECENTLY_UPDATED':
          comparison = compareNullableValues(
            parseTaskDate(left.updatedAt),
            parseTaskDate(right.updatedAt),
            direction)
          break
        case 'NEWEST_CREATED':
          comparison = compareNullableValues(
            parseTaskDate(left.createdAt),
            parseTaskDate(right.createdAt),
            direction)
          break
        case 'TITLE_ASC':
          comparison = compareTaskText(left.title, right.title) * direction
          break
        case 'TASK_TYPE':
          comparison = compareNullableValues(
            Number.isFinite(left.taskTypeSortOrder) ? left.taskTypeSortOrder : null,
            Number.isFinite(right.taskTypeSortOrder) ? right.taskTypeSortOrder : null,
            direction)
          comparison = comparison || compareTaskText(left.taskTypeName, right.taskTypeName) * direction
          break
        case 'STATUS':
          comparison = compareNullableValues(
            Number.isFinite(left.taskStatusSortOrder) ? left.taskStatusSortOrder : null,
            Number.isFinite(right.taskStatusSortOrder) ? right.taskStatusSortOrder : null,
            direction)
          comparison = comparison || compareTaskText(left.taskStatusName, right.taskStatusName) * direction
          break
      }

      return comparison || compareTaskTieBreakers(left, right)
    })
  }

  function getVisibleTasks() {
    const query = getTaskSearchQuery()
    const selectedTags = getSelectedTaskTagFilters()
    const taskTypeCode = getTaskListFilterValue('#task-type-filter')
    const taskPriorityCode = getTaskListFilterValue('#task-priority-filter')

    const filteredTasks = tasks.filter(function (task) {
      const taskTags = (task.tags || []).map(function (tag) {
        return String(tag).toLocaleLowerCase()
      })
      const matchesSearch = !query
        || task.title.toLowerCase().includes(query)
          || task.taskTypeName.toLowerCase().includes(query)
          || task.taskStatusName.toLowerCase().includes(query)
          || (task.taskPriorityName || '').toLowerCase().includes(query)
          || (task.activeWaitingForLabel || '').toLowerCase().includes(query)
          || (task.owner || '').toLowerCase().includes(query)
          || (task.responsible || '').toLowerCase().includes(query)
          || taskTags.some(function (tag) { return tag.includes(query) })
      const matchesTags = selectedTags.length === 0
        || selectedTags.some(function (selectedTag) {
          return taskTags.includes(selectedTag)
        })
      const matchesTaskType = !taskTypeCode || task.taskTypeCode === taskTypeCode
      const matchesPriority = !taskPriorityCode || task.taskPriorityCode === taskPriorityCode

      return matchesSearch && matchesTags && matchesTaskType && matchesPriority
    })

    return sortTasksForCurrentView(filteredTasks)
  }

  function renderTaskList() {
    const query = getTaskSearchQuery()
    const selectedTags = getSelectedTaskTagFilters()
    const taskTypeCode = getTaskListFilterValue('#task-type-filter')
    const taskPriorityCode = getTaskListFilterValue('#task-priority-filter')
    const visibleTasks = getVisibleTasks()

    syncTaskSortControl()
    const hasFilters = query.length > 0
      || selectedTags.length > 0
      || taskTypeCode.length > 0
      || taskPriorityCode.length > 0
    const countLabel = hasFilters
      ? `${visibleTasks.length} of ${tasks.length} ${tasks.length === 1 ? 'task' : 'tasks'}`
      : `${visibleTasks.length} ${visibleTasks.length === 1 ? 'task' : 'tasks'}`
    renderTaskFilterSummary(countLabel)
    $('#task-view').val(currentView)
    $('#task-list-title').text(viewLabels[currentView])
    $('#task-list-header-count').text(visibleTasks.length)
    const $activeViewButton = $('.task-view-rail-button')
      .removeClass('is-active is-transition-destination')
      .attr('aria-current', null)
      .filter(`[data-task-view="${currentView}"]`)
      .addClass('is-active')
      .attr('aria-current', 'page')
    if (taskTransitionReveal && taskTransitionReveal.view === currentView) {
      $activeViewButton.addClass('is-transition-destination')
    }

    if (visibleTasks.length === 0) {
      const title = hasFilters ? 'No matching tasks' : `No ${viewLabels[currentView].toLowerCase()} tasks`
      const detail = hasFilters ? 'Adjust the search or filters' : 'Create a task or switch view'

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
      const cancelledClass = task.taskStatusCode === 'CANCELLED' ? ' is-cancelled' : ''
      const transitionReveal = getTaskTransitionReveal(task)
      const transitionClass = transitionReveal
        ? ` is-transition-reveal is-transition-${transitionReveal.kind}`
        : ''
      const transitionLabel = transitionReveal
        ? `
          <span class="task-row-transition-label">
            <span class="fluent-icon" aria-hidden="true">${transitionReveal.icon}</span>
            <span>${encodeText(transitionReveal.rowLabel)}</span>
          </span>
        `
        : ''
      const priority = task.taskPriorityName
        ? renderBadge(task.taskPriorityName, task.taskPriorityBackgroundColor, task.taskPriorityForegroundColor)
        : ''
      const deadline = task.deadline
        ? `<span class="task-badge${isTaskOverdue(task) ? ' task-badge-overdue' : ''}">Due ${encodeText(formatShortDate(task.deadline))}</span>`
        : ''
      const waiting = task.activeWaitingForLabel
        ? `<span class="task-badge task-badge-waiting">Waiting: ${encodeText(task.activeWaitingForLabel)}</span>`
        : ''
      const checklistProgress = task.checklistCount > 0
        ? `<span class="task-badge">${task.completedChecklistCount}/${task.checklistCount}</span>`
        : ''

      return `
        <button class="task-row${selectedClass}${waitingClass}${cancelledClass}${transitionClass}" type="button" data-task-id="${task.id}"${selectedClass ? ' aria-current="true"' : ''}>
          ${transitionLabel}
          <span class="task-row-title">${encodeText(task.title)}</span>
          <span class="task-row-meta">
            ${renderBadge(task.taskTypeName, task.taskTypeBackgroundColor, task.taskTypeForegroundColor)}
            ${renderTaskStatusBadge(task)}
            ${priority}
            ${deadline}
            ${waiting}
            ${checklistProgress}
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

  function revealTaskRow(taskId) {
    const taskList = document.querySelector('#task-list')
    const taskRow = document.querySelector(`#task-list .task-row[data-task-id="${taskId}"]`)
    if (!taskList || !taskRow) {
      return false
    }

    const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches
    window.requestAnimationFrame(function () {
      const documentScroller = document.scrollingElement
      const editorPanel = document.querySelector('.task-editor-panel')
      const workspaceShell = document.querySelector('.workspace-shell')
      const scrollState = {
        documentLeft: documentScroller ? documentScroller.scrollLeft : window.scrollX,
        documentTop: documentScroller ? documentScroller.scrollTop : window.scrollY,
        editorLeft: editorPanel ? editorPanel.scrollLeft : 0,
        editorTop: editorPanel ? editorPanel.scrollTop : 0,
        workspaceLeft: workspaceShell ? workspaceShell.scrollLeft : 0,
        workspaceTop: workspaceShell ? workspaceShell.scrollTop : 0,
        taskListLeft: taskList.scrollLeft,
        taskListTop: taskList.scrollTop
      }

      try {
        taskRow.focus({ preventScroll: true })
      } catch {
        taskRow.focus()
      }

      if (documentScroller) {
        documentScroller.scrollLeft = scrollState.documentLeft
        documentScroller.scrollTop = scrollState.documentTop
      } else {
        window.scrollTo(scrollState.documentLeft, scrollState.documentTop)
      }
      if (editorPanel) {
        editorPanel.scrollLeft = scrollState.editorLeft
        editorPanel.scrollTop = scrollState.editorTop
      }
      if (workspaceShell) {
        workspaceShell.scrollLeft = scrollState.workspaceLeft
        workspaceShell.scrollTop = scrollState.workspaceTop
      }
      taskList.scrollLeft = scrollState.taskListLeft
      taskList.scrollTop = scrollState.taskListTop

      const listBox = taskList.getBoundingClientRect()
      const rowBox = taskRow.getBoundingClientRect()
      const centeredTop = taskList.scrollTop
        + (rowBox.top - listBox.top)
        - ((taskList.clientHeight - rowBox.height) / 2)
      const maximumTop = Math.max(0, taskList.scrollHeight - taskList.clientHeight)
      const targetTop = Math.max(0, Math.min(maximumTop, centeredTop))

      taskList.scrollTo({
        top: targetTop,
        left: scrollState.taskListLeft,
        behavior: reduceMotion ? 'auto' : 'smooth'
      })
    })

    return true
  }

  function ensureTaskVisibleForTransition(taskId) {
    const isVisible = function () {
      return getVisibleTasks().some(function (task) {
        return task.id === taskId
      })
    }

    if (isVisible()) {
      return true
    }

    if (getTaskSearchQuery()) {
      $('#task-search').val('')
      renderTaskList()
    }

    if (isVisible()) {
      return true
    }

    $('#task-tag-filter').val([]).trigger('change.select2')
    $('#task-type-filter, #task-priority-filter').val('')
    setTaskFilterPopoverOpen(false, false)
    renderTaskList()
    return isVisible()
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
      taskSourceCode: null,
      sourceReference: null,
      sourceUrl: null,
      deadline: null,
      activeWaitingForLabel: null,
      tags: []
    }
  }

  function switchTaskView(targetView) {
    if (!Object.hasOwn(viewLabels, targetView) || targetView === currentView) {
      return
    }

    allowContextSwitch().then(function (isAllowed) {
      if (!isAllowed) {
        $('#task-view').val(currentView)
        return
      }

      clearTaskTransitionReveal(false)
      currentView = targetView
      syncTaskSortControl()
      loadTasks({ selectFirst: true }).catch(showFatalError)
    })
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
    setEditorHeightControlEnabled(false)
    $('#save-button').prop('disabled', true)

    const editorMode = modeCode === 'MARKDOWN' ? 'markdown' : 'html'

    await window.Editor.initialize({
      mode: editorMode,
      selector: '#text-body',
      hostSelector: '#editor-host',
      baseUrl: '/tinymce',
      minHeight: preferredEditorHeight,
      markdownEditType: preferredMarkdownEditType,
      colorScheme: layoutPreference.colorScheme,
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
    setEditorHeightControlEnabled(true)
    applyCurrentTaskEditability()

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
    const canEditWaiting = isTaskEditable(task)
      && (!task.id || task.taskStatusCode === 'ACTIVE')

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
      setTaskOwnedControlsEnabled(!!currentTask?.id && isTaskEditable(currentTask))
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
    setTaskOwnedControlsEnabled(!!currentTask?.id && isTaskEditable(currentTask))
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
    setTaskOwnedControlsEnabled(isTaskEditable(currentTask))
  }

  async function addComment() {
    if (!currentTask || !currentTask.id || !canMutateCurrentTask()) {
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
    if (!currentTask || !currentTask.id || !commentId || !canMutateCurrentTask()) {
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

    if (currentTask && currentTask.id) {
      await loadRelationships(currentTask.id)
    }

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
      foregroundColor: $('#lookup-edit-foreground-color').val().toString(),
      reverseName: null
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

    if (!await showConfirmationDialog(
      'Delete lookup value?',
      `Delete “${item.name}”? This cannot be undone.`,
      'Delete lookup')) {
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

  function isTaskEditable(task) {
    if (!task) {
      return false
    }

    if (!task.id) {
      return true
    }

    if (task.taskStatusCode === 'COMPLETED') {
      return layoutPreference.allowEditingCompletedTasks
    }

    if (task.taskStatusCode === 'CANCELLED') {
      return layoutPreference.allowEditingCancelledTasks
    }

    return true
  }

  function canMutateCurrentTask() {
    if (isTaskEditable(currentTask)) {
      return true
    }

    setStatus('Reopen task to edit', 'ready')
    return false
  }

  function applyCurrentTaskEditability() {
    if (!currentTask) {
      $('#task-read-only-notice').prop('hidden', true)
      $('#task-form').removeClass('is-task-read-only').attr('aria-readonly', null)
      return
    }

    const isEditable = isTaskEditable(currentTask)
    const isCompleted = currentTask.taskStatusCode === 'COMPLETED'
    const stateName = isCompleted ? 'Completed' : 'Cancelled'
    const isReadOnlyFinalTask = !isEditable
      && (isCompleted || currentTask.taskStatusCode === 'CANCELLED')

    $('#task-read-only-notice').prop('hidden', !isReadOnlyFinalTask)
    $('#task-read-only-title').text(`${stateName} task — read only`)
    $('#task-read-only-message').text(
      'Reopen this task to make changes. History, relationships, and attachments remain available to review.')
    $('#task-form')
      .toggleClass('is-task-read-only', isReadOnlyFinalTask)
      .attr('aria-readonly', isReadOnlyFinalTask ? 'true' : null)
    $('#task-form input, #task-form select').prop('disabled', !isEditable)
    $('#save-button')
      .prop('disabled', !isEditable)
      .attr('title', isEditable ? null : 'Reopen this task to edit it')
    $('#editor-host').attr('aria-readonly', String(!isEditable))

    if (window.Editor && isEditorReady && typeof window.Editor.setReadOnly === 'function') {
      window.Editor.setReadOnly(!isEditable)
    }

    setTaskOwnedControlsEnabled(!!currentTask.id && isEditable)
    renderWaitingPanel(currentTask)
  }

  function renderTaskHeaderAndActions(task) {
    const isSavedTask = !!task.id
    const isFinal = task.taskStatusCode === 'COMPLETED' || task.taskStatusCode === 'CANCELLED'
    const canReopen = isSavedTask && isFinal
    const canCompleteOrCancel = isSavedTask && !isFinal
    const waitingLabel = task.activeWaitingFor ? describeWaiting(task.activeWaitingFor) : ''
    const transitionReveal = getTaskTransitionReveal(task)
    const contextLabel = transitionReveal
      ? transitionReveal.headerLabel
      : waitingLabel
        ? `Waiting · ${waitingLabel}`
        : task.taskStatusName || 'Draft'

    $('#task-editor-title').text(task.id ? 'Task details' : 'New task')
    $('#task-status-label')
      .text(contextLabel)
      .attr('title', contextLabel)
      .toggleClass('is-waiting-context', !transitionReveal && waitingLabel.length > 0)
      .toggleClass('is-transition-context', !!transitionReveal)
      .toggleClass('is-transition-completed', !!transitionReveal && transitionReveal.kind === 'completed')
      .toggleClass('is-transition-cancelled', !!transitionReveal && transitionReveal.kind === 'cancelled')
      .toggleClass('is-transition-active', !!transitionReveal && transitionReveal.kind === 'active')
    $('#complete-button')
      .text(canReopen ? 'Reopen' : 'Complete')
      .prop('disabled', !(canCompleteOrCancel || canReopen))
    $('#cancel-button').prop('disabled', !canCompleteOrCancel)
    applyCurrentTaskEditability()
  }

  function setTaskOwnedControlsEnabled(isEnabled) {
    $('#attachment-add-button, #attachment-file').prop('disabled', !isEnabled)
    $('#checklist-new-text, #checklist-add-button').prop('disabled', !isEnabled)
    $('#relationship-type, #relationship-task, #relationship-add-button').prop('disabled', !isEnabled)
    $('#relationships-list .relationship-delete').prop('disabled', !isEnabled)
    $('#checklist-list .checklist-completed, #checklist-list .checklist-text, #checklist-list .checklist-delete')
      .prop('disabled', !isEnabled)
    $('#checklist-list .checklist-move-up, #checklist-list .checklist-move-down').prop('disabled', !isEnabled)
    if (isEnabled) {
      $('#checklist-list .checklist-row:first .checklist-move-up').prop('disabled', true)
      $('#checklist-list .checklist-row:last .checklist-move-down').prop('disabled', true)
    }
    $('#attachment-list .attachment-delete-button').prop('disabled', !isEnabled)
    $('#timeline-list .timeline-delete-comment-button').prop('disabled', !isEnabled)
    setCommentControlsEnabled(isEnabled)
  }

  function requireSavedTaskId(task) {
    const taskId = Number(task && task.id)
    if (!Number.isSafeInteger(taskId) || taskId <= 0) {
      throw new Error('Task creation did not return a saved task ID.')
    }

    return taskId
  }

  function refreshCurrentTaskWithoutEditor(task, options) {
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
      $('#task-owner').val(task.owner || '')
      $('#task-responsible').val(task.responsible || '')
      setTaskTags(task.tags || [])
      $('#editor-mode').val(getSupportedBodyFormatCode(preferredBodyFormatCode))
      $('#task-form input, #task-form select').prop('disabled', false)
      $('#editor-mode').prop('disabled', false)
      renderTaskHeaderAndActions(task)
      if (!(options && options.skipTaskListRender)) {
        renderTaskList()
      }
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
      $('#task-owner').val(task.owner || '')
      $('#task-responsible').val(task.responsible || '')
      setTaskTags(task.tags || [])
      $('#editor-mode').val(getSupportedBodyFormatCode(preferredBodyFormatCode))
      renderWaitingPanel(task)

      $('#task-form input, #task-form select').prop('disabled', false)
      $('#editor-mode').prop('disabled', false)
      renderTaskHeaderAndActions(task)

      await initializeEditorForTask(task)
      await loadRelationships(task.id)
      await loadChecklist(task.id)
      await loadAttachments(task.id)
      await loadTimeline(task.id)
      applyCurrentTaskEditability()
      isDirty = false
      setCleanTaskSnapshot()
      window.Editor.markClean()
      setStatus(task.id && !isTaskEditable(task) ? 'Read only' : task.id ? 'Loaded' : 'Draft', 'ready')
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
      owner: $('#task-owner').val().toString().trim() || null,
      responsible: $('#task-responsible').val().toString().trim() || null,
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
        syncPreferenceControls()
      }

      return
    }

    const activeModeCode = window.Editor.getMode() === 'markdown' ? 'MARKDOWN' : 'HTML'

    if (targetModeCode === activeModeCode) {
      syncPreferenceControls()
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
      syncPreferenceControls()
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
    const revealTaskId = options && Number(options.revealTaskId)
    tasks = await sendBridgeMessage('task.list', {
      view: currentView
    })
    renderTaskList()

    if (keepSelection && currentTask && currentTask.id) {
      const stillVisible = tasks.some(function (task) {
        return task.id === currentTask.id
      })
      if (stillVisible) {
        if (Number.isSafeInteger(revealTaskId) && revealTaskId === currentTask.id) {
          ensureTaskVisibleForTransition(revealTaskId)
          revealTaskRow(revealTaskId)
        }
        return currentTask
      }

      if (Number.isSafeInteger(revealTaskId) && revealTaskId === currentTask.id) {
        throw new Error(`Updated task was not returned by the ${viewLabels[currentView]} view.`)
      }
    }

    if (selectFirst) {
      if (tasks.length > 0) {
        const firstVisibleTask = getVisibleTasks()[0]
        if (!firstVisibleTask) {
          renderEmptyEditor()
          focusTaskList()
          return null
        }

        await selectTask(firstVisibleTask.id)
        focusTaskRow(firstVisibleTask.id)
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
    if (taskTransitionReveal && taskTransitionReveal.taskId !== Number(id)) {
      clearTaskTransitionReveal(false)
    }

    const task = await sendBridgeMessage('task.get', {
      id
    })
    await renderTaskEditor(task)
  }

  async function saveTask() {
    if (!currentTask) {
      return false
    }
    if (!canMutateCurrentTask()) {
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
    selectViewForTask(task)
    beginTaskTransitionReveal(task)
    refreshCurrentTaskWithoutEditor(task, { skipTaskListRender: true })
    await loadTimeline(task.id)
    await loadTasks({
      keepSelection: true,
      revealTaskId: task.id
    })
    setStatus(isTaskEditable(task) ? 'Loaded' : 'Read only', 'ready')
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

  function renderHelpDocument(topic, html) {
    if (topic !== activeHelpTopic) {
      return
    }

    const article = document.createElement('article')
    article.className = 'help-document'
    article.innerHTML = html

    article.querySelectorAll('a[href]').forEach(function (link) {
      const href = link.getAttribute('href') || ''
      const linkedTopic = href.includes('mcp-server.md')
        ? 'mcp-server'
        : href.includes('okf-layer.md')
          ? 'okf-layer'
          : null

      if (linkedTopic) {
        link.setAttribute('href', `#help-${linkedTopic}`)
        link.setAttribute('data-help-topic-link', linkedTopic)
        return
      }

      if (!/^[a-z][a-z\d+.-]*:/i.test(href) && !href.startsWith('#')) {
        const reference = document.createElement('span')
        reference.className = 'help-reference-text'
        reference.textContent = link.textContent
        reference.title = 'Open this reference from the repository or installed OKF bundle.'
        link.replaceWith(reference)
      }
    })

    $('#help-content').empty().append(article).scrollTop(0)
  }

  async function loadHelpTopic(topic, forceReload) {
    if (!Object.prototype.hasOwnProperty.call(helpTopics, topic)) {
      return
    }

    activeHelpTopic = topic
    $('.help-topic-button')
      .removeClass('is-active')
      .removeAttr('aria-current')
      .filter(`[data-help-topic="${topic}"]`)
      .addClass('is-active')
      .attr('aria-current', 'page')

    if (!forceReload && helpDocumentCache.has(topic)) {
      renderHelpDocument(topic, helpDocumentCache.get(topic))
      return
    }

    $('#help-content').html('<p class="help-loading">Loading help...</p>')

    try {
      const helpUrl = `${helpTopics[topic]}?v=${Date.now()}`
      const response = await window.fetch(helpUrl, { cache: 'no-store' })
      if (!response.ok) {
        throw new Error(`Help request failed with status ${response.status}.`)
      }

      const markdown = await response.text()
      const html = await window.Editor.renderMarkdown(markdown)
      helpDocumentCache.set(topic, html)
      renderHelpDocument(topic, html)
    } catch (error) {
      if (topic !== activeHelpTopic) {
        return
      }

      $('#help-content').html(`
        <div class="help-load-error" role="alert">
          <h1>Help could not be loaded</h1>
          <p>${encodeText(getErrorMessage(error, 'The local help document is unavailable.'))}</p>
          <button class="secondary-button help-retry-button" type="button" data-help-retry="${encodeAttribute(topic)}">Retry</button>
        </div>
      `)
    }
  }

  function openHelp() {
    $('#help-overlay').prop('hidden', false)
    $('#help-close-button').trigger('focus')
    loadHelpTopic(activeHelpTopic, true)
  }

  function closeHelp() {
    $('#help-overlay').prop('hidden', true)
    $('#help-button').trigger('focus')
  }

  function syncPreferenceChoiceGroup(selectId) {
    const $select = $(`#${selectId}`)
    const selectedValue = String($select.val() || '')
    const isDisabled = $select.prop('disabled')
    const $group = $(`[data-preference-select="${selectId}"]`)

    $group.find('.preference-choice').each(function () {
      const isSelected = String($(this).attr('data-value')) === selectedValue
      $(this)
        .toggleClass('is-selected', isSelected)
        .attr('aria-pressed', String(isSelected))
        .prop('disabled', isDisabled)
    })
  }

  function syncPreferenceControls() {
    syncPreferenceChoiceGroup('editor-mode')
    syncPreferenceChoiceGroup('color-scheme')
    syncPreferenceChoiceGroup('layout-mode')
  }

  function setActivePreferenceSection(section) {
    if (!Object.prototype.hasOwnProperty.call(preferenceSectionLabels, section)) {
      return
    }

    activePreferenceSection = section
    $('.preferences-nav-button').each(function () {
      const isActive = $(this).attr('data-preference-section') === section
      $(this)
        .toggleClass('is-active', isActive)
        .attr('aria-current', isActive ? 'page' : null)
    })
    $('#preferences-panel-title').text(preferenceSectionLabels[section])
    $('[data-preference-panel]').each(function () {
      $(this).prop('hidden', $(this).attr('data-preference-panel') !== section)
    })
    $('.preferences-content').scrollTop(0)
  }

  function choosePreferenceValue(button) {
    const $button = $(button)
    const selectId = $button.closest('[data-preference-select]').attr('data-preference-select')
    const $select = $(`#${selectId}`)

    if (!selectId || $select.prop('disabled')) {
      return
    }

    $select.val($button.attr('data-value')).trigger('change')
    syncPreferenceChoiceGroup(selectId)
  }

  function openSettings() {
    $('#settings-overlay').prop('hidden', false)
    setActivePreferenceSection(activePreferenceSection)
    syncPreferenceControls()
    $('#settings-close-button').trigger('focus')
    loadLookupSettings().catch(function (error) {
      setStatus(getErrorMessage(error, 'Could not load lookup settings'), 'error')
      $('#lookup-settings-groups').html('<div class="empty-lookup-settings">Could not load lookup values.</div>')
    })
    loadTagSettings().catch(function (error) {
      setStatus(getErrorMessage(error, 'Could not load tags'), 'error')
      $('#tag-settings-count').text('!')
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
    $('#new-task-save-button').text('Saving')
    setStatus('Saving', 'ready')

    try {
      const createdTask = await sendBridgeMessage('task.create', payload)
      const taskId = requireSavedTaskId(createdTask)
      const savedTask = await sendBridgeMessage('task.get', { id: taskId })
      requireSavedTaskId(savedTask)
      await renderTaskEditor(savedTask)
      closeNewTaskDialog()
      window.Editor.focus()
      selectViewForTask(savedTask)
      await loadTasks({ keepSelection: true })
      isDirty = false
      window.Editor.markClean()
      setStatus('Saved', 'saved')
    } catch (error) {
      $('#new-task-save-button, #new-task-cancel-button').prop('disabled', false)
      throw error
    } finally {
      $('#new-task-save-button').text('Save')
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

    $('#help-button').on('click', openHelp)
    $('#help-close-button').on('click', closeHelp)
    $('#help-overlay').on('click', function (event) {
      if (event.target === this) {
        closeHelp()
      }
    })
    $('.help-topic-button').on('click', function () {
      loadHelpTopic($(this).attr('data-help-topic'), false)
    })
    $('#help-content').on('click', '.help-retry-button', function () {
      loadHelpTopic($(this).attr('data-help-retry'), true)
    })
    $('#help-content').on('click', 'a[data-help-topic-link]', function (event) {
      event.preventDefault()
      loadHelpTopic($(this).attr('data-help-topic-link'), false)
    })

    $('#settings-button').on('click', openSettings)
    $('#settings-close-button').on('click', closeSettings)
    $('#settings-overlay').on('click', function (event) {
      if (event.target === this) {
        closeSettings()
      }
    })
    $('.preferences-nav-button').on('click', function () {
      setActivePreferenceSection($(this).attr('data-preference-section'))
    })
    $('#settings-overlay').on('click', '.preference-choice', function () {
      choosePreferenceValue(this)
    })
    $('#settings-overlay').on('click', '.lookup-group-button[data-lookup-group]', function () {
      openLookupList($(this).attr('data-lookup-group')).catch(function (error) {
        setStatus(getErrorMessage(error, 'Could not open lookup values'), 'error')
      })
    })
    $('#tag-settings-button').on('click', function () {
      openTagList().catch(function (error) {
        setStatus(getErrorMessage(error, 'Could not load tags'), 'error')
      })
    })
    $('#tag-list-close-button, #tag-list-done-button').on('click', closeTagList)
    $('#tag-list-overlay').on('click', function (event) {
      if (event.target === this) closeTagList()
    })
    $('#tag-list-items').on('click', '.tag-list-edit-button', function () {
      openTagEdit(Number($(this).attr('data-tag-id')))
    })
    $('#tag-edit-cancel-button').on('click', closeTagEdit)
    $('#tag-edit-overlay').on('click', function (event) {
      if (event.target === this) closeTagEdit()
    })
    $('#tag-edit-value, #tag-merge-target').on('input change', function () {
      $(this).removeClass('is-invalid')
      $('#tag-edit-error').text('').prop('hidden', true)
    })
    $('#tag-edit-save-button').on('click', function () {
      saveTagEdit().catch(function (error) {
        $('#tag-edit-error').text(getErrorMessage(error, 'Could not rename tag')).prop('hidden', false)
        setStatus(getErrorMessage(error, 'Could not rename tag'), 'error')
      })
    })
    $('#tag-edit-delete-button').on('click', function () {
      deleteTagEdit().catch(function (error) {
        $('#tag-edit-error').text(getErrorMessage(error, 'Could not delete tag')).prop('hidden', false)
        setStatus(getErrorMessage(error, 'Could not delete tag'), 'error')
      })
    })
    $('#tag-edit-merge-button').on('click', function () {
      mergeTagEdit().catch(function (error) {
        $('#tag-edit-error').text(getErrorMessage(error, 'Could not merge tags')).prop('hidden', false)
        setStatus(getErrorMessage(error, 'Could not merge tags'), 'error')
      })
    })
    $('#tag-edit-value').on('keydown', function (event) {
      if (event.key === 'Enter') {
        event.preventDefault()
        saveTagEdit().catch(function (error) {
          $('#tag-edit-error').text(getErrorMessage(error, 'Could not rename tag')).prop('hidden', false)
          setStatus(getErrorMessage(error, 'Could not rename tag'), 'error')
        })
      }
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
    $('#confirmation-cancel-button').on('click', function () { resolveConfirmationDialog(false) })
    $('#confirmation-confirm-button').on('click', function () { resolveConfirmationDialog(true) })
    $('#confirmation-overlay').on('click', function (event) {
      if (event.target === this) resolveConfirmationDialog(false)
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
      if (event.key === 'Escape' && !$('#confirmation-overlay').prop('hidden')) {
        resolveConfirmationDialog(false)
        return
      }
      if (event.key === 'Escape' && !$('#lookup-edit-overlay').prop('hidden')) {
        closeLookupEdit()
        return
      }
      if (event.key === 'Escape' && !$('#tag-edit-overlay').prop('hidden')) {
        closeTagEdit()
        return
      }
      if (event.key === 'Escape' && !$('#tag-list-overlay').prop('hidden')) {
        closeTagList()
        return
      }
      if (event.key === 'Escape' && !$('#lookup-list-overlay').prop('hidden')) {
        closeLookupList()
        return
      }
      if (event.key === 'Escape' && !$('#help-overlay').prop('hidden')) {
        closeHelp()
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

    $('#task-view').on('change', function () {
      switchTaskView($(this).val().toString())
    })

    $('.task-view-rail').on('click', '.task-view-rail-button', function () {
      switchTaskView($(this).attr('data-task-view'))
    })

    $('#task-search').on('input', renderTaskList)
    $('#task-tag-filter').on('change', renderTaskList)
    $('#task-type-filter, #task-priority-filter').on('change', renderTaskList)
    $('#task-filter-button').on('click', function () {
      const isOpen = $('#task-filter-popover').prop('hidden')
      setTaskFilterPopoverOpen(isOpen, isOpen)
    })
    $('#task-filter-summary').on('click', '.task-filter-chip', function () {
      const filter = $(this).attr('data-filter')
      if (filter) {
        $(`#${filter}-filter`).val('')
        renderTaskList()
        return
      }

      const tag = $(this).attr('data-tag')
      const selected = getSelectedTaskTagFilterValues().filter(function (value) {
        return value !== tag
      })
      $('#task-tag-filter').val(selected).trigger('change.select2')
      renderTaskList()
    })
    $('#task-filter-clear').on('click', clearTaskFilters)
    $(document).on('click', function (event) {
      if (!$(event.target).closest('.task-filter-menu').length) {
        setTaskFilterPopoverOpen(false, false)
      }
    })
    $(document).on('keydown', function (event) {
      if (event.key === 'Escape' && !$('#task-filter-popover').prop('hidden')) {
        event.preventDefault()
        setTaskFilterPopoverOpen(false, false)
        $('#task-filter-button').trigger('focus')
        return
      }

      if ((event.ctrlKey || event.metaKey)
        && event.key.toLowerCase() === 'k'
        && !$(event.target).closest('.task-editor-panel, .modal-overlay').length) {
        event.preventDefault()
        $('#task-search').trigger('focus').trigger('select')
      }
    })
    $('#task-sort').on('change', function () {
      taskSortModes[currentView] = getTaskSortOption($(this).val().toString()).code
      layoutPreference.taskSortModes = taskSortModes
      syncTaskSortControl()
      renderTaskList()
      scheduleLayoutPreferenceSave()
    })
    $('#task-sort-direction').on('click', function () {
      taskSortDirections[currentView] = getCurrentTaskSortDirection() === taskSortDirectionCodes.ascending
        ? taskSortDirectionCodes.descending
        : taskSortDirectionCodes.ascending
      layoutPreference.taskSortDirections = taskSortDirections
      syncTaskSortControl()
      renderTaskList()
      scheduleLayoutPreferenceSave()
    })
    $('#relationship-add-button').on('click', function () {
      addRelationship().catch(function (error) { setStatus(getErrorMessage(error, 'Could not add relationship'), 'error') })
    })
    $('#relationships-list').on('click', '.relationship-open', function () {
      selectTaskWithUnsavedCheck(Number($(this).attr('data-task-id')))
    })
    $('#relationships-list').on('click', '.relationship-delete', async function () {
      const row = $(this).closest('.relationship-row')
      const title = row.find('.relationship-open').text()
      if (!await showConfirmationDialog('Remove relationship?', `Remove the relationship to “${title}”?`, 'Remove relationship')) return
      deleteRelationship(Number(row.attr('data-relation-id')))
        .catch(function (error) { setStatus(getErrorMessage(error, 'Could not remove relationship'), 'error') })
    })
    $('#checklist-add-button').on('click', function () {
      addChecklistItem().catch(function (error) { setStatus(getErrorMessage(error, 'Could not add checklist item'), 'error') })
    })
    $('#checklist-new-text').on('keydown', function (event) {
      if (event.key === 'Enter') {
        event.preventDefault()
        addChecklistItem().catch(function (error) { setStatus(getErrorMessage(error, 'Could not add checklist item'), 'error') })
      }
    })
    $('#checklist-list').on('change', '.checklist-completed', function () {
      const row = $(this).closest('.checklist-row')
      setChecklistItemCompleted(Number(row.attr('data-checklist-item-id')), $(this).prop('checked'))
        .catch(function (error) { setStatus(getErrorMessage(error, 'Could not update checklist item'), 'error') })
    })
    $('#checklist-list').on('change', '.checklist-text', function () {
      const row = $(this).closest('.checklist-row')
      updateChecklistItem(Number(row.attr('data-checklist-item-id')), $(this).val().toString())
        .catch(function (error) { setStatus(getErrorMessage(error, 'Could not update checklist item'), 'error') })
    })
    $('#checklist-list').on('click', '.checklist-move-up, .checklist-move-down', function () {
      const row = $(this).closest('.checklist-row')
      moveChecklistItem(Number(row.attr('data-checklist-item-id')), $(this).hasClass('checklist-move-up') ? -1 : 1)
        .catch(function (error) { setStatus(getErrorMessage(error, 'Could not reorder checklist'), 'error') })
    })
    $('#checklist-list').on('click', '.checklist-delete', async function () {
      const row = $(this).closest('.checklist-row')
      const text = row.find('.checklist-text').val().toString()
      if (!await showConfirmationDialog('Delete checklist item?', `Delete “${text}”? This cannot be undone.`, 'Delete item')) return
      deleteChecklistItem(Number(row.attr('data-checklist-item-id')))
        .catch(function (error) { setStatus(getErrorMessage(error, 'Could not delete checklist item'), 'error') })
    })
    $('#attachment-add-button').on('click', function () { $('#attachment-file').trigger('click') })
    $('#attachment-file').on('change', function () {
      addAttachment().catch(function (error) { setStatus(getErrorMessage(error, 'Could not add attachment'), 'error') })
    })
    $('#attachment-list').on('click', '.attachment-download-button', function () {
      downloadAttachment(Number($(this).attr('data-attachment-id'))).catch(function (error) { setStatus(getErrorMessage(error, 'Could not download attachment'), 'error') })
    })
    $('#attachment-list').on('click', '.attachment-delete-button', async function () {
      const fileName = $(this).closest('.attachment-row').find('.attachment-name').text()
      if (!await showConfirmationDialog('Remove attachment?', `Remove “${fileName}”? This cannot be undone.`, 'Remove file')) return
      deleteAttachment(Number($(this).attr('data-attachment-id'))).catch(function (error) { setStatus(getErrorMessage(error, 'Could not remove attachment'), 'error') })
    })
    $('#task-form').on('input change', '#task-title, #task-type, #task-priority, #task-deadline, #task-tags, #task-source, #task-source-reference, #task-source-url, #task-owner, #task-responsible, #waiting-text', markDirty)
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
    $('#timeline-list').on('click', '.timeline-delete-comment-button', async function () {
      if (!await showConfirmationDialog('Delete comment?', 'Delete this comment? This cannot be undone.', 'Delete comment')) return
      deleteComment(Number($(this).attr('data-comment-id'))).catch(function (error) {
        setStatus(getErrorMessage(error, 'Could not delete comment'), 'error')
      })
    })
    $('#editor-mode').on('change', function () {
      switchEditorMode().catch(function (error) {
        setStatus(getErrorMessage(error, 'Could not switch editor'), 'error')
      })
    })
    const editorHeightResizer = document.getElementById('editor-height-resizer')
    editorHeightResizer.addEventListener('pointerdown', beginEditorHeightResize)
    editorHeightResizer.addEventListener('pointermove', updateEditorHeightResize)
    editorHeightResizer.addEventListener('pointerup', finishEditorHeightResize)
    editorHeightResizer.addEventListener('pointercancel', cancelEditorHeightResize)
    editorHeightResizer.addEventListener('lostpointercapture', function (event) {
      if (editorHeightDragState && editorHeightDragState.pointerId === event.pointerId) {
        finishEditorHeightResize(event)
      }
    })
    $('#editor-height-resizer').on('keydown', resizeEditorFromKeyboard)
    $('#layout-mode').on('change', switchLayoutMode)
    $('#color-scheme').on('change', switchColorScheme)
    $('#show-source-fields, #show-owner, #show-responsible, #show-relationships, #allow-editing-completed-tasks, #allow-editing-cancelled-tasks')
      .on('change', saveTaskDetailPreference)
    $('#backup-database-button').on('click', function () {
      backupDatabase().catch(function (error) {
        setStatus(getErrorMessage(error, 'Could not back up database'), 'error')
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
    $('#task-read-only-reopen-button').on('click', function () {
      runLifecycleAction('task.reopen').catch(function (error) {
        setStatus(getErrorMessage(error, 'Could not reopen task'), 'error')
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
