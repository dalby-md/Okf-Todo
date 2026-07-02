(function ($) {
  const issueId = 1
  const defaultTitle = 'Untitled text'
  const defaultBody = '<p>Start writing your text here.</p>'
  const pendingRequests = new Map()
  const editorImageSources = new Map()
  const bridgeTimeoutMs = 15000

  let currentIssue = null
  let isEditorReady = false

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

  function getEditor() {
    return window.tinymce ? window.tinymce.get('text-body') : null
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
          <textarea id="text-body"></textarea>
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

  async function createImageFromFile(file) {
    const base64Data = await fileToBase64(file)
    const result = await sendBridgeMessage('image.create', {
      issueId,
      filename: file.name,
      mimeType: file.type,
      base64Data,
      width: null,
      height: null
    })

    const dataUrl = `data:${file.type};base64,${base64Data}`
    editorImageSources.set(dataUrl, result.src)

    return {
      dataUrl,
      appSrc: result.src
    }
  }

  function insertImageFromFile(callback) {
    const input = document.createElement('input')
    input.type = 'file'
    input.accept = 'image/png,image/jpeg,image/gif,image/webp'

    input.addEventListener('change', async function () {
      const file = input.files && input.files[0]

      if (!file) {
        return
      }

      try {
        setStatus('Saving image', 'ready')
        const image = await createImageFromFile(file)
        callback(image.dataUrl, {
          alt: file.name,
          'data-app-src': image.appSrc
        })
        markDirty()
      } catch (error) {
        setStatus(error.message || 'Could not save image', 'error')
      }
    })

    input.click()
  }

  async function saveDocument() {
    const editor = getEditor()

    if (!editor || !currentIssue) {
      setStatus('Editor is still loading', 'error')
      return
    }

    const title = $('#text-title').val().toString().trim() || defaultTitle
    const bodyHtml = normalizeImagesForSave(editor.getContent({ format: 'html' }))

    try {
      const issue = await sendBridgeMessage('issue.save', {
        id: currentIssue.id,
        title,
        status: currentIssue.status || 'Open',
        priority: currentIssue.priority || 0,
        dueUtc: currentIssue.dueUtc || null,
        bodyHtml
      })

      currentIssue = issue
      $('#text-title').val(issue.title)
      editor.setDirty(false)
      setStatus('Saved to SQLite', 'saved')
    } catch (error) {
      setStatus(error.message || 'Could not save document', 'error')
    }
  }

  async function resetDocument() {
    if (!window.confirm('Reset this text to the starter content?')) {
      return
    }

    const editor = getEditor()
    $('#text-title').val(defaultTitle)

    if (editor) {
      editor.setContent(defaultBody)
      editor.setDirty(false)
    }

    currentIssue = await sendBridgeMessage('issue.save', {
      id: issueId,
      title: defaultTitle,
      status: 'Open',
      priority: 0,
      dueUtc: null,
      bodyHtml: defaultBody
    })

    setStatus('Reset and saved to SQLite', 'saved')
  }

  async function initializeEditor() {
    if (!window.tinymce) {
      setStatus('TinyMCE did not load', 'error')
      return
    }

    currentIssue = await sendBridgeMessage('issue.get', {
      id: issueId
    })

    $('#text-title').val(currentIssue.title || defaultTitle)
    $('#text-body').val(await resolveStoredImages(currentIssue.bodyHtml || defaultBody))

    await window.tinymce.init({
      selector: '#text-body',
      base_url: '/tinymce',
      suffix: '.min',
      license_key: 'gpl',
      menubar: false,
      branding: false,
      promotion: false,
      plugins: 'autoresize code image link lists table',
      toolbar:
        'undo redo | blocks | bold italic underline | bullist numlist | blockquote | link image table | code',
      automatic_uploads: true,
      file_picker_types: 'image',
      file_picker_callback: insertImageFromFile,
      images_upload_handler: async function (blobInfo) {
        const image = await createImageFromFile(blobInfo.blob())
        return image.dataUrl
      },
      min_height: 420,
      autoresize_bottom_margin: 0,
      content_style:
        'body { color: #202124; font-family: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif; font-size: 16px; line-height: 1.55; }',
      setup: function (editor) {
        editor.on('change keyup undo redo setcontent', markDirty)
      }
    })

    const editor = getEditor()
    if (editor) {
      editor.setDirty(false)
    }

    isEditorReady = true
    $('#save-button').prop('disabled', false)
    setStatus('Loaded from SQLite', 'ready')
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
    $('#reset-button').on('click', function () {
      resetDocument().catch(function (error) {
        setStatus(error.message || 'Could not reset document', 'error')
      })
    })
  })
})(jQuery)
