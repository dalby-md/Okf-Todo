(function ($) {
  const issueId = 1
  const defaultTitle = 'Untitled text'
  const defaultBody = '<p>Start writing your text here.</p>'
  const defaultMarkdown = 'Start writing your text here.'
  const pendingRequests = new Map()
  const editorImageSources = new Map()
  const bridgeTimeoutMs = 15000

  let currentIssue = null
  let isEditorReady = false
  let currentEditorMode = 'html'

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
    let registeredReceiver = false

    try {
      if (window.external && typeof window.external.receiveMessage === 'function') {
        window.external.receiveMessage(receiveBridgeMessage)
        registeredReceiver = true
      }
    } catch (error) {
      setStatus(error.message || 'Could not register Photino bridge receiver', 'error')
    }

    try {
      if (window.chrome && window.chrome.webview) {
        window.chrome.webview.addEventListener('message', function (event) {
          receiveBridgeMessage(event.data)
        })
        registeredReceiver = true
      }
    } catch (error) {
      setStatus(error.message || 'Could not register WebView bridge receiver', 'error')
    }

    return registeredReceiver
  }

  function setStatus(message, state) {
    $('#save-status')
      .removeClass('is-ready is-dirty is-saved is-error')
      .addClass(state ? `is-${state}` : '')
      .text(message)
  }

  function showFatalError(message) {
    $('#app').html(`
      <main class="app-shell">
        <section class="editor-panel" aria-labelledby="app-title">
          <header class="app-header">
            <div>
              <p class="eyebrow">SQLite HTML editor</p>
              <h1 id="app-title">OKF Text</h1>
            </div>
          </header>
          <p id="fatal-error" class="empty-state"></p>
        </section>
      </main>
    `)
    $('#fatal-error').text(message)
  }

  function markDirty() {
    if (isEditorReady) {
      setStatus('Unsaved changes', 'dirty')
    }
  }

  function renderShell() {
    $('#app').html(`
      <main class="app-shell">
        <section class="editor-panel" aria-labelledby="app-title">
          <header class="app-header">
            <div>
              <p class="eyebrow">SQLite HTML editor</p>
              <h1 id="app-title">OKF Text</h1>
            </div>
            <div class="app-actions" aria-label="Document actions">
              <span id="save-status" class="save-status is-ready" role="status">Loading editor</span>
              <label class="mode-label" for="editor-mode">Editor</label>
              <select id="editor-mode" class="mode-select" disabled>
                <option value="html">HTML editor</option>
                <option value="markdown">TOAST UI Markdown</option>
              </select>
              <button id="reset-button" class="secondary-button" type="button">Reset</button>
              <button id="save-button" type="button" disabled>Save</button>
            </div>
          </header>

          <label class="field-label" for="text-title">Title</label>
          <input
            id="text-title"
            class="title-input"
            type="text"
            autocomplete="off"
          />

          <label class="field-label" for="text-body">Body</label>
          <div id="editor-host">
            <textarea id="text-body"></textarea>
          </div>
        </section>
      </main>
    `)
  }

  function fileToBase64(file) {
    return new Promise(function (resolve, reject) {
      const reader = new FileReader()
      reader.addEventListener('load', function () {
        const result = reader.result.toString()
        resolve(result.slice(result.indexOf(',') + 1))
      })
      reader.addEventListener('error', function () {
        reject(reader.error || new Error('Could not read image file.'))
      })
      reader.readAsDataURL(file)
    })
  }

  function imageToDataUrl(image) {
    return `data:${image.mimeType};base64,${image.base64Data}`
  }

  function getImageIdFromAppSrc(src) {
    const match = /^app:\/\/image\/(\d+)$/i.exec(src || '')
    return match ? Number(match[1]) : null
  }

  async function resolveStoredImages(bodyHtml) {
    const $container = $('<div>').html(bodyHtml || defaultBody)
    const imageElements = $container.find('img').toArray()

    for (const imageElement of imageElements) {
      const $image = $(imageElement)
      const appSrc = $image.attr('src')
      const imageId = getImageIdFromAppSrc(appSrc)

      if (!imageId) {
        continue
      }

      const image = await sendBridgeMessage('image.get', {
        id: imageId
      })
      const dataUrl = imageToDataUrl(image)
      editorImageSources.set(dataUrl, appSrc)
      $image.attr('src', dataUrl)
      $image.attr('data-app-src', appSrc)
    }

    return $container.html()
  }

  async function resolveStoredMarkdownImages(markdown) {
    const replacements = []
    const imageExpression = /!\[([^\]]*)\]\(app:\/\/image\/(\d+)\)/gi
    let match

    while ((match = imageExpression.exec(markdown || defaultMarkdown)) !== null) {
      replacements.push({
        original: match[0],
        alt: match[1],
        imageId: Number(match[2])
      })
    }

    let resolvedMarkdown = markdown || defaultMarkdown

    for (const replacement of replacements) {
      const image = await sendBridgeMessage('image.get', {
        id: replacement.imageId
      })
      const appSrc = `app://image/${replacement.imageId}`
      const dataUrl = imageToDataUrl(image)
      editorImageSources.set(dataUrl, appSrc)
      resolvedMarkdown = resolvedMarkdown.replace(
        replacement.original,
        `![${replacement.alt}](${dataUrl})`
      )
    }

    return resolvedMarkdown
  }

  function normalizeImagesForSave(bodyHtml) {
    const $container = $('<div>').html(bodyHtml || '')

    $container.find('img').each(function () {
      const $image = $(this)
      const appSrc = $image.attr('data-app-src') || editorImageSources.get($image.attr('src'))

      if (appSrc) {
        $image.attr('src', appSrc)
        $image.removeAttr('data-app-src')
      }
    })

    return $container.html()
  }

  function normalizeMarkdownImagesForSave(markdown) {
    let normalizedMarkdown = markdown || ''

    editorImageSources.forEach(function (appSrc, dataUrl) {
      normalizedMarkdown = normalizedMarkdown.split(dataUrl).join(appSrc)
    })

    return normalizedMarkdown
  }

  async function createImageFromFile(file) {
    const base64Data = await fileToBase64(file)
    const dataUrl = `data:${file.type};base64,${base64Data}`
    const existingAppSrc = editorImageSources.get(dataUrl)

    if (existingAppSrc) {
      return {
        dataUrl,
        appSrc: existingAppSrc
      }
    }

    const result = await sendBridgeMessage('image.create', {
      issueId,
      filename: file.name || 'image',
      mimeType: file.type,
      base64Data,
      width: null,
      height: null
    })

    editorImageSources.set(dataUrl, result.src)

    return {
      dataUrl,
      appSrc: result.src
    }
  }

  function pickImageFromFile(existingFile) {
    const input = document.createElement('input')

    return new Promise(function (resolve, reject) {
      if (existingFile) {
        createImageFromFile(existingFile)
          .then(function (image) {
            resolve({
              src: image.dataUrl,
              attributes: {
                alt: existingFile.name || '',
                'data-app-src': image.appSrc
              }
            })
          })
          .catch(reject)
        return
      }

      input.type = 'file'
      input.accept = 'image/png,image/jpeg,image/gif,image/webp'

      input.addEventListener('change', async function () {
        const file = input.files && input.files[0]

        if (!file) {
          resolve(null)
          return
        }

        try {
          setStatus('Saving image', 'ready')
          const image = await createImageFromFile(file)
          markDirty()
          resolve({
            src: image.dataUrl,
            attributes: {
              alt: file.name,
              'data-app-src': image.appSrc
            }
          })
        } catch (error) {
          setStatus(error.message || 'Could not save image', 'error')
          reject(error)
        }
      })

      input.click()
    })
  }

  async function saveDocument() {
    if (!isEditorReady || !currentIssue) {
      setStatus('Editor is still loading', 'error')
      return
    }

    const title = $('#text-title').val().toString().trim() || defaultTitle
    const bodyHtml = normalizeImagesForSave(window.Editor.getHtml())
    const bodyMarkdown = currentEditorMode === 'markdown'
      ? normalizeMarkdownImagesForSave(window.Editor.getMarkdown())
      : currentIssue.bodyMarkdown || ''

    try {
      const issue = await sendBridgeMessage('issue.save', {
        id: currentIssue.id,
        title,
        status: currentIssue.status || 'Open',
        priority: currentIssue.priority || 0,
        dueUtc: currentIssue.dueUtc || null,
        bodyHtml,
        bodyMarkdown,
        editorMode: currentEditorMode
      })

      currentIssue = issue
      $('#text-title').val(issue.title)
      window.Editor.markClean()
      setStatus('Saved to SQLite', 'saved')
    } catch (error) {
      setStatus(error.message || 'Could not save document', 'error')
    }
  }

  async function resetDocument() {
    if (!window.confirm('Reset this text to the starter content?')) {
      return
    }

    $('#text-title').val(defaultTitle)

    window.Editor.load(currentEditorMode === 'markdown' ? defaultMarkdown : defaultBody)
    window.Editor.markClean()

    currentIssue = await sendBridgeMessage('issue.save', {
      id: issueId,
      title: defaultTitle,
      status: 'Open',
      priority: 0,
      dueUtc: null,
      bodyHtml: defaultBody,
      bodyMarkdown: defaultMarkdown,
      editorMode: currentEditorMode
    })

    setStatus('Reset and saved to SQLite', 'saved')
  }

  async function initializeEditor() {
    if (!window.Editor) {
      setStatus('Editor service did not load', 'error')
      return
    }

    currentIssue = await sendBridgeMessage('issue.get', {
      id: issueId
    })

    currentEditorMode = currentIssue.editorMode === 'markdown' ? 'markdown' : 'html'
    $('#editor-mode').val(currentEditorMode)
    $('#text-title').val(currentIssue.title || defaultTitle)

    const hasStoredMarkdown = !!(currentIssue.bodyMarkdown && currentIssue.bodyMarkdown.trim())
    const initialHtml = currentEditorMode === 'html' || !hasStoredMarkdown
      ? await resolveStoredImages(currentIssue.bodyHtml || defaultBody)
      : null
    const initialMarkdown = currentEditorMode === 'markdown' && hasStoredMarkdown
      ? await resolveStoredMarkdownImages(currentIssue.bodyMarkdown)
      : defaultMarkdown

    window.Editor.onChanged(markDirty)
    await window.Editor.initialize({
      mode: currentEditorMode,
      selector: '#text-body',
      hostSelector: '#editor-host',
      baseUrl: '/tinymce',
      minHeight: 420,
      initialContent: currentEditorMode === 'markdown' ? initialMarkdown : initialHtml,
      initialHtml: currentEditorMode === 'markdown' && !hasStoredMarkdown ? initialHtml : null,
      contentStyle:
        'body { color: #202124; font-family: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif; font-size: 16px; line-height: 1.55; }',
      onPickImage: pickImageFromFile
    })

    window.Editor.markClean()
    isEditorReady = true
    $('#save-button').prop('disabled', false)
    $('#editor-mode').prop('disabled', false)
    setStatus('Loaded from SQLite', 'ready')
  }

  async function changeEditorMode(nextMode) {
    if (nextMode !== 'html' && nextMode !== 'markdown') {
      return
    }

    if (nextMode === currentEditorMode) {
      return
    }

    if (currentIssue) {
      if (currentEditorMode === 'markdown') {
        currentIssue.bodyMarkdown = normalizeMarkdownImagesForSave(window.Editor.getMarkdown())
      } else {
        currentIssue.bodyHtml = normalizeImagesForSave(window.Editor.getHtml())
      }
    }

    isEditorReady = false
    currentEditorMode = nextMode
    $('#save-button').prop('disabled', true)
    $('#editor-mode').prop('disabled', true)
    setStatus('Switching editor', 'ready')

    const hasStoredMarkdown = !!(currentIssue && currentIssue.bodyMarkdown && currentIssue.bodyMarkdown.trim())
    const initialHtml = currentEditorMode === 'html' || !hasStoredMarkdown
      ? await resolveStoredImages((currentIssue && currentIssue.bodyHtml) || defaultBody)
      : null
    const initialMarkdown = currentEditorMode === 'markdown' && hasStoredMarkdown
      ? await resolveStoredMarkdownImages(currentIssue.bodyMarkdown)
      : defaultMarkdown

    await window.Editor.initialize({
      mode: currentEditorMode,
      selector: '#text-body',
      hostSelector: '#editor-host',
      baseUrl: '/tinymce',
      minHeight: 420,
      initialContent: currentEditorMode === 'markdown' ? initialMarkdown : initialHtml,
      initialHtml: currentEditorMode === 'markdown' && !hasStoredMarkdown ? initialHtml : null,
      contentStyle:
        'body { color: #202124; font-family: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif; font-size: 16px; line-height: 1.55; }',
      onPickImage: pickImageFromFile
    })

    isEditorReady = true
    $('#save-button').prop('disabled', false)
    $('#editor-mode').prop('disabled', false)
    markDirty()
  }

  $(async function () {
    renderShell()
    initializeBridgeReceiver()

    try {
      await initializeEditor()
    } catch (error) {
      showFatalError(error.message || 'The saved document could not be loaded.')
      return
    }

    $('#text-title').on('input', markDirty)
    $('#save-button').on('click', saveDocument)
    $('#editor-mode').on('change', function () {
      changeEditorMode($(this).val().toString()).catch(function (error) {
        setStatus(error.message || 'Could not switch editor', 'error')
        $('#editor-mode').val(currentEditorMode).prop('disabled', false)
        $('#save-button').prop('disabled', false)
      })
    })
    $('#reset-button').on('click', function () {
      resetDocument().catch(function (error) {
        setStatus(error.message || 'Could not reset document', 'error')
      })
    })
  })
})(jQuery)
