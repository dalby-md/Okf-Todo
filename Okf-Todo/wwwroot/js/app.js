(function ($) {
  const pendingRequests = new Map()
  const bridgeTimeoutMs = 15000
  const viewLabels = {
    inbox: 'Inbox',
    active: 'Active',
    completed: 'Completed',
    all: 'All'
  }

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

    return new Promise(function (resolve, reject) {
      const timeoutId = window.setTimeout(function () {
        pendingRequests.delete(messageId)
        reject(new Error(`Timed out waiting for ${type}.`))
      }, bridgeTimeoutMs)

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

  function setFieldInvalid(selector, isInvalid) {
    $(selector).toggleClass('is-invalid', isInvalid)
  }

  function clearValidationState() {
    $('#task-title, #task-type').removeClass('is-invalid')
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
      deadline: null
    }
  }

  async function initializeEditorForTask(task) {
    if (!window.Editor) {
      throw new Error('Editor service did not load.')
    }

    isEditorReady = false
    $('#save-button').prop('disabled', true)

    const modeCode = task.bodyFormatCode === 'MARKDOWN' ? 'MARKDOWN' : 'HTML'
    const editorMode = modeCode === 'MARKDOWN' ? 'markdown' : 'html'

    await window.Editor.initialize({
      mode: editorMode,
      selector: '#text-body',
      hostSelector: '#editor-host',
      baseUrl: '/tinymce',
      minHeight: 360,
      initialContent: task.body || '',
      contentStyle:
        'body { color: #202124; font-family: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif; font-size: 15px; line-height: 1.55; }',
      onPickImage: async function () {
        setStatus('Images are not part of this UI slice', 'error')
        return null
      }
    })

    window.Editor.markClean()
    isEditorReady = true
    $('#save-button').prop('disabled', false)
  }

  async function renderTaskEditor(task) {
    currentTask = task
    isDirty = false
    clearValidationState()

    $('#task-editor-title').text(task.id ? task.title : 'New task')
    $('#task-status-label').text(task.taskStatusName || 'Draft')
    $('#task-title').val(task.title || '')
    $('#task-type').val(task.taskTypeCode || '')
    $('#task-priority').val(task.taskPriorityCode || '')
    $('#task-deadline').val(formatDate(task.deadline))
    $('#task-source').val(task.taskSourceCode || '')
    $('#task-source-reference').val(task.sourceReference || '')
    $('#task-source-url').val(task.sourceUrl || '')
    $('#editor-mode').val(task.bodyFormatCode || 'HTML')

    $('#task-form input, #task-form select').prop('disabled', false)
    $('#editor-mode').prop('disabled', true)
    $('#start-button').prop('disabled', !(task.id && task.taskStatusCode === 'NEW'))
    $('#complete-button').prop('disabled', !(task.id && (task.taskStatusCode === 'NEW' || task.taskStatusCode === 'ACTIVE')))
    $('#cancel-button').prop('disabled', !(task.id && task.taskStatusCode !== 'COMPLETED' && task.taskStatusCode !== 'CANCELLED'))

    await initializeEditorForTask(task)
    setStatus(task.id ? 'Loaded' : 'Draft', 'ready')
    renderTaskList()
  }

  function getEditorBody() {
    const modeCode = $('#editor-mode').val().toString()
    return modeCode === 'MARKDOWN'
      ? window.Editor.getMarkdown()
      : window.Editor.getHtml()
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
      await renderTaskEditor(savedTask)
      await loadTasks({ keepSelection: true })
      isDirty = false
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

    const task = await sendBridgeMessage(type, {
      id: currentTask.id
    })
    await renderTaskEditor(task)
    await loadTasks({ keepSelection: true })
    setStatus('Updated', 'saved')
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
    $('#task-form').on('input change', 'input, select', markDirty)
    $('#save-button').on('click', function () {
      saveTask().catch(function (error) {
        setStatus(getErrorMessage(error, 'Could not save task'), 'error')
      })
    })
    $('#start-button').on('click', function () {
      runLifecycleAction('task.start').catch(function (error) {
        setStatus(getErrorMessage(error, 'Could not start task'), 'error')
      })
    })
    $('#complete-button').on('click', function () {
      runLifecycleAction('task.complete').catch(function (error) {
        setStatus(getErrorMessage(error, 'Could not complete task'), 'error')
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
