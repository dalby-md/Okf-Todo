(function (window, document) {
  const changedCallbacks = []
  const defaultSelector = '#text-body'

  let activeAdapter = null
  let activeMode = 'html'
  let activeInitialization = 0
  let activeColorScheme = 'LIGHT'
  let pickImage = null
  const loadedAssets = new Map()

  function ensureStyle(href) {
    if (document.querySelector(`link[href="${href}"]`)) {
      return
    }

    const link = document.createElement('link')
    link.rel = 'stylesheet'
    link.href = href
    document.head.appendChild(link)
  }

  function loadScript(src) {
    if (loadedAssets.has(src)) {
      return loadedAssets.get(src)
    }

    const promise = new Promise(function (resolve, reject) {
      const script = document.createElement('script')
      const timeoutId = window.setTimeout(function () {
        loadedAssets.delete(src)
        reject(new Error(`Timed out loading ${src}.`))
      }, 15000)

      script.src = src
      script.onload = function () {
        window.clearTimeout(timeoutId)
        resolve()
      }
      script.onerror = function () {
        window.clearTimeout(timeoutId)
        loadedAssets.delete(src)
        reject(new Error(`Could not load ${src}.`))
      }
      document.head.appendChild(script)
    })

    loadedAssets.set(src, promise)
    return promise
  }

  async function ensureToastUiLoaded() {
    ensureStyle('/codemirror/codemirror.css')
    ensureStyle('/toastui/toastui-editor.css')

    if (!window.CodeMirror) {
      await loadScript('/codemirror/codemirror.js')
    }

    await loadScript('/codemirror/xml.js')
    await loadScript('/codemirror/markdown.js')
    await loadScript('/codemirror/gfm.js')

    if (!window.toastui || !window.toastui.Editor) {
      await loadScript('/toastui/toastui-editor.js')
    }
  }

  async function ensureTinyMceLoaded() {
    if (!window.tinymce) {
      await loadScript('/tinymce/tinymce.min.js')
    }
  }

  function notifyChanged() {
    changedCallbacks.forEach(function (callback) {
      callback()
    })
  }

  function requireAdapter() {
    if (!activeAdapter) {
      throw new Error('Editor is not initialized.')
    }

    return activeAdapter
  }

  async function renderMarkdown(markdown) {
    await ensureToastUiLoaded()

    if (!window.toastui || !window.toastui.Editor || typeof window.toastui.Editor.factory !== 'function') {
      throw new Error('TOAST UI Markdown viewer is unavailable.')
    }

    const host = document.createElement('div')
    const viewer = window.toastui.Editor.factory({
      el: host,
      viewer: true,
      initialValue: String(markdown || ''),
      usageStatistics: false
    })
    const html = host.innerHTML

    if (viewer && typeof viewer.destroy === 'function') {
      viewer.destroy()
    }

    return html
  }

  function getElementId(selector) {
    return selector.replace(/^#/, '')
  }

  function getHost(options) {
    const hostSelector = options.hostSelector || '#editor-host'
    const host = document.querySelector(hostSelector)

    if (!host) {
      throw new Error(`Editor host '${hostSelector}' was not found.`)
    }

    return host
  }

  function encodeAttribute(value) {
    return String(value)
      .replace(/&/g, '&amp;')
      .replace(/"/g, '&quot;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
  }

  function encodeText(value) {
    return String(value)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
  }

  function escapeMarkdown(value) {
    return String(value || '').replace(/([\\`*_{}[\]()#+\-.!|>])/g, '\\$1')
  }

  function toAttributeHtml(attributes) {
    return Object.keys(attributes || {})
      .filter(function (name) {
        return attributes[name] !== null && attributes[name] !== undefined
      })
      .map(function (name) {
        return `${name}="${encodeAttribute(attributes[name])}"`
      })
      .join(' ')
  }

  async function pickAndInsertImage(callback) {
    if (typeof pickImage !== 'function') {
      throw new Error('Image picker is not configured.')
    }

    const image = await pickImage()

    if (!image || !image.src) {
      return
    }

    callback(image.src, image.attributes || {})
  }

  function wrapSelection(editor, before, after, placeholder) {
    const codeMirror = typeof editor.getCodeMirror === 'function' ? editor.getCodeMirror() : null

    if (!codeMirror) {
      editor.insertText(`${before}${placeholder || ''}${after}`)
      return
    }

    const selectedText = codeMirror.getSelection() || placeholder || ''
    codeMirror.replaceSelection(`${before}${selectedText}${after}`)
    codeMirror.focus()
  }

  function insertMarkdown(editor, text) {
    const codeMirror = typeof editor.getCodeMirror === 'function' ? editor.getCodeMirror() : null

    if (codeMirror) {
      codeMirror.replaceSelection(text)
      codeMirror.focus()
      return
    }

    editor.insertText(text)
  }

  function icon(name) {
    const icons = {
      undo: '<path d="M3 7v6h6"></path><path d="M21 17a9 9 0 0 0-15-6.7L3 13"></path>',
      redo: '<path d="M21 7v6h-6"></path><path d="M3 17a9 9 0 0 1 15-6.7l3 2.7"></path>',
      heading: '<path d="M6 4v16"></path><path d="M18 4v16"></path><path d="M6 12h12"></path>',
      bold: '<path d="M7 4h6a4 4 0 0 1 0 8H7z"></path><path d="M7 12h7a4 4 0 0 1 0 8H7z"></path>',
      italic: '<path d="M19 4h-9"></path><path d="M14 20H5"></path><path d="M15 4 9 20"></path>',
      ul: '<path d="M8 6h13"></path><path d="M8 12h13"></path><path d="M8 18h13"></path><path d="M3 6h.01"></path><path d="M3 12h.01"></path><path d="M3 18h.01"></path>',
      ol: '<path d="M10 6h11"></path><path d="M10 12h11"></path><path d="M10 18h11"></path><path d="M4 6h1v4"></path><path d="M4 10h2"></path><path d="M4 14h2l-2 4h2"></path>',
      quote: '<path d="M3 21c3 0 7-1 7-8V5H4v8h3c0 3-1 5-4 5z"></path><path d="M14 21c3 0 7-1 7-8V5h-6v8h3c0 3-1 5-4 5z"></path>',
      link: '<path d="M10 13a5 5 0 0 0 7.1 0l2-2a5 5 0 0 0-7.1-7.1l-1.1 1.1"></path><path d="M14 11a5 5 0 0 0-7.1 0l-2 2a5 5 0 0 0 7.1 7.1l1.1-1.1"></path>',
      image: '<rect x="3" y="5" width="18" height="14" rx="2"></rect><circle cx="8.5" cy="10.5" r="1.5"></circle><path d="m21 15-5-5L5 21"></path>',
      table: '<rect x="3" y="4" width="18" height="16" rx="2"></rect><path d="M3 10h18"></path><path d="M9 4v16"></path><path d="M15 4v16"></path>',
      code: '<path d="m8 9-4 3 4 3"></path><path d="m16 9 4 3-4 3"></path><path d="m14 4-4 16"></path>'
    }

    return `<svg class="toolbar-icon" aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">${icons[name]}</svg>`
  }

  function toolbarButton(command, label, iconName) {
    return `<button type="button" data-markdown-command="${command}" title="${label}" aria-label="${label}">${icon(iconName)}<span class="sr-only">${label}</span></button>`
  }

  function destroyActiveAdapter() {
    if (!activeAdapter) {
      return
    }

    activeAdapter.destroy()
    activeAdapter = null
  }

  function setHostEditorHeight(host, height) {
    const nextHeight = Math.round(Number(height))
    if (!Number.isFinite(nextHeight) || nextHeight <= 0) {
      return null
    }

    host.style.setProperty('--editor-height', `${nextHeight}px`)
    host.style.height = `${nextHeight}px`
    host.style.minHeight = `${nextHeight}px`
    return nextHeight
  }

  function clearHostEditorHeight(host) {
    host.style.removeProperty('--editor-height')
    host.style.height = ''
    host.style.minHeight = ''
  }

  function getOuterBlockSize(element) {
    if (!element) {
      return 0
    }

    const rect = element.getBoundingClientRect()
    const style = window.getComputedStyle(element)
    const marginTop = parseFloat(style.marginTop) || 0
    const marginBottom = parseFloat(style.marginBottom) || 0

    return Math.ceil(rect.height + marginTop + marginBottom)
  }

  function getToastUiEditorHeight(host, totalHeight) {
    const toolbarHeight = getOuterBlockSize(host.querySelector('.markdown-toolbar'))
    return Math.max(120, totalHeight - toolbarHeight)
  }

  function createTinyMceAdapter(options) {
    let editor = null
    let readOnly = false
    const selector = options.selector || defaultSelector
    const elementId = getElementId(selector)
    const host = getHost(options)

    host.innerHTML = `
      <div class="editor-loading" role="status">Loading editor...</div>
      <textarea id="${encodeAttribute(elementId)}"></textarea>
    `
    const textarea = document.querySelector(selector)
    if (!textarea) {
      throw new Error('Editor textarea was not created.')
    }

    textarea.value = options.initialContent || ''

    function applyColorScheme(colorScheme) {
      if (!editor || typeof editor.getBody !== 'function') {
        return
      }

      const body = editor.getBody()
      const isDark = String(colorScheme || '').toUpperCase() === 'DARK'
      body.style.setProperty('color', isDark ? '#e7e5e4' : '#202124')
      body.style.setProperty('background-color', isDark ? '#101112' : '#ffffff')
    }

    function applyReadOnlyState() {
      host.classList.toggle('is-read-only', readOnly)

      if (!editor) {
        return
      }

      editor.mode.set(readOnly ? 'readonly' : 'design')

      const body = typeof editor.getBody === 'function' ? editor.getBody() : null
      if (body) {
        body.setAttribute('contenteditable', String(!readOnly))
        body.setAttribute('aria-readonly', String(readOnly))
      }

      const container = typeof editor.getContainer === 'function'
        ? editor.getContainer()
        : null
      if (container) {
        container.querySelectorAll('.tox-toolbar button').forEach(function (button) {
          button.disabled = readOnly
          button.setAttribute('aria-disabled', String(readOnly))
        })
      }
    }

    return {
      initialize: async function () {
        await ensureTinyMceLoaded()

        await window.tinymce.init({
          target: textarea,
          base_url: options.baseUrl || '/tinymce',
          suffix: '.min',
          license_key: 'gpl',
          menubar: false,
          branding: false,
          promotion: false,
          plugins: 'image link lists',
          toolbar:
            'undo redo | blocks | bold italic underline | bullist numlist | blockquote | link image',
          automatic_uploads: true,
          file_picker_types: 'image',
          file_picker_callback: pickAndInsertImage,
          images_upload_handler: async function (blobInfo) {
            if (typeof pickImage !== 'function') {
              throw new Error('Image upload is not configured.')
            }

            const image = await pickImage(blobInfo.blob())
            return image.src
          },
          height: options.minHeight || 420,
          resize: false,
          content_style: options.contentStyle || '',
          setup: function (tinyEditor) {
            editor = tinyEditor
            tinyEditor.on('init', function () {
              applyColorScheme(activeColorScheme)
              applyReadOnlyState()
            })
            tinyEditor.on('change keyup undo redo setcontent', notifyChanged)
          }
        })

        editor = window.tinymce.get(elementId)
        if (!editor) {
          throw new Error('TinyMCE did not attach to the editor textarea.')
        }

        applyColorScheme(activeColorScheme)
        applyReadOnlyState()

        const loading = host.querySelector('.editor-loading')
        if (loading) {
          loading.remove()
        }
      },

      load: function (html) {
        editor.setContent(html || '')
      },

      getHtml: function () {
        return editor.getContent({ format: 'html' })
      },

      getMarkdown: function () {
        return ''
      },

      getMarkdownEditType: function () {
        return 'MARKDOWN'
      },

      insertImage: function (src, attributes) {
        if (readOnly) return
        const imageAttributes = Object.assign({}, attributes || {}, { src })
        const attributeHtml = toAttributeHtml(imageAttributes)
        editor.insertContent(`<img ${attributeHtml}>`)
      },

      insertLink: function (url, text) {
        if (readOnly) return
        const selectedText = text || editor.selection.getContent({ format: 'text' }) || url
        editor.insertContent(`<a href="${encodeAttribute(url)}">${encodeText(selectedText)}</a>`)
      },

      getSelectedText: function () {
        return editor.selection.getContent({ format: 'text' })
      },

      setSelectedText: function (text) {
        if (readOnly) return
        editor.insertContent(encodeText(text || ''))
      },

      execute: function (command, value) {
        if (readOnly) return
        editor.execCommand(command, false, value)
      },

      setReadOnly: function (value) {
        readOnly = value === true
        applyReadOnlyState()
        window.requestAnimationFrame(applyReadOnlyState)
      },

      setColorScheme: function (colorScheme) {
        applyColorScheme(colorScheme)
      },

      focus: function () {
        editor.focus()
      },

      setHeight: function (height) {
        const nextHeight = setHostEditorHeight(host, height)
        if (!nextHeight || !editor) {
          return
        }

        const container = typeof editor.getContainer === 'function'
          ? editor.getContainer()
          : null
        const contentArea = typeof editor.getContentAreaContainer === 'function'
          ? editor.getContentAreaContainer()
          : null
        const chromeHeight = container && contentArea
          ? Math.max(0, container.offsetHeight - contentArea.offsetHeight)
          : 0

        if (container) {
          container.style.height = `${nextHeight}px`
        }

        if (contentArea) {
          contentArea.style.height = `${Math.max(80, nextHeight - chromeHeight)}px`
        }
      },

      markClean: function () {
        editor.setDirty(false)
      },

      destroy: function () {
        clearHostEditorHeight(host)
        host.classList.remove('is-read-only')

        if (editor) {
          if (typeof editor.destroy === 'function') {
            editor.destroy()
          } else if (typeof editor.remove === 'function') {
            editor.remove()
          }
          editor = null
        }
        host.innerHTML = ''
      }
    }
  }

  function createToastUiAdapter(options) {
    let editor = null
    let readOnly = false
    let suppressChangeUntil = 0
    const host = getHost(options)
    const initialMarkdownEditType = String(options.markdownEditType || '').toLowerCase() === 'wysiwyg'
      ? 'wysiwyg'
      : 'markdown'

    function suppressModeSwitchChanges() {
      suppressChangeUntil = Date.now() + 2500
      if (typeof options.onMarkdownEditTypeChanging === 'function') {
        options.onMarkdownEditTypeChanging()
      }
    }

    function getCurrentMarkdownEditType() {
      if (!editor) {
        return initialMarkdownEditType === 'wysiwyg' ? 'WYSIWYG' : 'MARKDOWN'
      }

      return editor.isWysiwygMode && editor.isWysiwygMode() ? 'WYSIWYG' : 'MARKDOWN'
    }

    function applyReadOnlyState() {
      host.classList.toggle('is-read-only', readOnly)
      host.querySelectorAll('[data-markdown-command]').forEach(function (button) {
        button.disabled = readOnly
      })

      if (!editor) {
        return
      }

      const codeMirror = typeof editor.getCodeMirror === 'function'
        ? editor.getCodeMirror()
        : null
      if (codeMirror && typeof codeMirror.setOption === 'function') {
        codeMirror.setOption('readOnly', readOnly)
      }

      const squire = typeof editor.getSquire === 'function'
        ? editor.getSquire()
        : null
      const root = squire && typeof squire.getRoot === 'function'
        ? squire.getRoot()
        : null
      if (root) {
        root.setAttribute('contenteditable', String(!readOnly))
        root.setAttribute('aria-readonly', String(readOnly))
      }
    }

    function notifyMarkdownEditTypeChanged(markdownEditType) {
      if (typeof options.onMarkdownEditTypeChanged === 'function') {
        options.onMarkdownEditTypeChanged(markdownEditType || getCurrentMarkdownEditType())
      }
    }

    function handleModeSwitchIntent(event) {
      if (!event.target || !event.target.closest) {
        return
      }

      if (event.type === 'keydown' && event.key !== 'Enter' && event.key !== ' ') {
        return
      }

      const button = event.target.closest('.te-switch-button')
      if (!button) {
        return
      }

      suppressModeSwitchChanges()

      const markdownEditType = button.classList.contains('wysiwyg')
        ? 'WYSIWYG'
        : button.classList.contains('markdown')
          ? 'MARKDOWN'
          : null

      if (markdownEditType) {
        notifyMarkdownEditTypeChanged(markdownEditType)
      }
    }

    host.innerHTML = `
      <div class="markdown-toolbar" aria-label="Markdown formatting">
        ${toolbarButton('undo', 'Undo', 'undo')}
        ${toolbarButton('redo', 'Redo', 'redo')}
        ${toolbarButton('heading', 'Heading', 'heading')}
        ${toolbarButton('bold', 'Bold', 'bold')}
        ${toolbarButton('italic', 'Italic', 'italic')}
        ${toolbarButton('ul', 'Bulleted list', 'ul')}
        ${toolbarButton('ol', 'Numbered list', 'ol')}
        ${toolbarButton('quote', 'Quote', 'quote')}
        ${toolbarButton('link', 'Link', 'link')}
        ${toolbarButton('image', 'Image', 'image')}
        ${toolbarButton('table', 'Table', 'table')}
        ${toolbarButton('code', 'Code', 'code')}
      </div>
      <div id="markdown-body"></div>
    `

    function bindToolbar() {
      host.querySelectorAll('[data-markdown-command]').forEach(function (button) {
        button.addEventListener('click', async function () {
          if (readOnly) {
            return
          }

          const command = button.getAttribute('data-markdown-command')
          const codeMirror = typeof editor.getCodeMirror === 'function' ? editor.getCodeMirror() : null

          if (command === 'undo' && codeMirror) {
            codeMirror.undo()
            return
          }

          if (command === 'redo' && codeMirror) {
            codeMirror.redo()
            return
          }

          if (command === 'heading') {
            insertMarkdown(editor, '# ')
            return
          }

          if (command === 'bold') {
            wrapSelection(editor, '**', '**', 'bold text')
            return
          }

          if (command === 'italic') {
            wrapSelection(editor, '*', '*', 'italic text')
            return
          }

          if (command === 'ul') {
            insertMarkdown(editor, '- ')
            return
          }

          if (command === 'ol') {
            insertMarkdown(editor, '1. ')
            return
          }

          if (command === 'quote') {
            insertMarkdown(editor, '> ')
            return
          }

          if (command === 'link') {
            const url = window.prompt('Link URL')
            if (url) {
              wrapSelection(editor, '[', `](${url})`, 'link text')
            }
            return
          }

          if (command === 'image') {
            await pickAndInsertImage(function (src, attributes) {
              insertMarkdown(editor, `![${escapeMarkdown((attributes && attributes.alt) || 'image')}](${src})`)
            })
            return
          }

          if (command === 'table') {
            insertMarkdown(editor, '\n| Column 1 | Column 2 |\n| --- | --- |\n| Text | Text |\n')
            return
          }

          if (command === 'code') {
            wrapSelection(editor, '`', '`', 'code')
          }
        })
      })
    }

    return {
      initialize: async function () {
        await ensureToastUiLoaded()

        if (!window.toastui || !window.toastui.Editor) {
          throw new Error(
            `TOAST UI Editor did not load. CodeMirror: ${window.CodeMirror ? 'yes' : 'no'}, toastui: ${window.toastui ? 'yes' : 'no'}.`
          )
        }

        const initialHeight = setHostEditorHeight(host, options.minHeight || 420) || 420
        const initialToastUiHeight = getToastUiEditorHeight(host, initialHeight)
        const markdownBody = host.querySelector('#markdown-body')
        if (markdownBody) {
          markdownBody.style.height = `${initialToastUiHeight}px`
        }

        editor = new window.toastui.Editor({
          el: document.querySelector('#markdown-body'),
          height: `${initialToastUiHeight}px`,
          initialEditType: initialMarkdownEditType,
          previewStyle: 'vertical',
          initialValue: options.initialContent || '',
          hideModeSwitch: false,
          usageStatistics: false,
          toolbarItems: [],
          hooks: {
            addImageBlobHook: async function (blob, callback) {
              if (typeof pickImage !== 'function') {
                throw new Error('Image upload is not configured.')
              }

              const image = await pickImage(blob)
              if (image && image.src) {
                callback(image.src, (image.attributes && image.attributes.alt) || 'image')
                notifyChanged()
              }

              return false
            }
          }
        })

        host.addEventListener('pointerdown', handleModeSwitchIntent, true)
        host.addEventListener('keydown', handleModeSwitchIntent, true)

        if (options.initialHtml) {
          editor.setHtml(options.initialHtml, false)
        }

        if (initialMarkdownEditType === 'wysiwyg' && editor.isMarkdownMode && editor.isMarkdownMode()) {
          suppressModeSwitchChanges()
          editor.changeMode('wysiwyg', true)
        } else if (initialMarkdownEditType === 'markdown' && editor.isWysiwygMode && editor.isWysiwygMode()) {
          suppressModeSwitchChanges()
          editor.changeMode('markdown', true)
        }

        editor.on('changeModeBefore', function () {
          suppressModeSwitchChanges()
        })

        editor.on('changeMode', function (mode) {
          suppressModeSwitchChanges()
          const markdownEditType = mode === 'wysiwyg' ? 'WYSIWYG' : 'MARKDOWN'
          notifyMarkdownEditTypeChanged(markdownEditType)
          window.requestAnimationFrame(applyReadOnlyState)
        })

        editor.on('change', function () {
          if (Date.now() < suppressChangeUntil) {
            return
          }

          notifyChanged()
        })
        bindToolbar()
        applyReadOnlyState()
      },

      load: function (markdown) {
        editor.setMarkdown(markdown || '', false)
      },

      getHtml: function () {
        return editor.getHtml()
      },

      getMarkdown: function () {
        return editor.getMarkdown()
      },

      getMarkdownEditType: function () {
        return getCurrentMarkdownEditType()
      },

      insertImage: function (src, attributes) {
        if (readOnly) return
        const alt = escapeMarkdown((attributes && attributes.alt) || 'image')
        editor.insertText(`![${alt}](${src})`)
      },

      insertLink: function (url, text) {
        if (readOnly) return
        const selectedText = text || this.getSelectedText() || url
        editor.insertText(`[${escapeMarkdown(selectedText)}](${url})`)
      },

      getSelectedText: function () {
        return ''
      },

      setSelectedText: function (text) {
        if (readOnly) return
        editor.insertText(text || '')
      },

      execute: function (command, value) {
        if (readOnly) return
        if (command === 'Bold') {
          this.setSelectedText(`**${this.getSelectedText() || value || ''}**`)
          return
        }

        if (command === 'Italic') {
          this.setSelectedText(`*${this.getSelectedText() || value || ''}*`)
        }
      },

      setReadOnly: function (value) {
        readOnly = value === true
        applyReadOnlyState()
      },

      setColorScheme: function () {
        // TOAST UI is styled by the application theme stylesheet.
      },

      focus: function () {
        editor.focus()
      },

      setHeight: function (height) {
        const nextHeight = setHostEditorHeight(host, height)
        if (!nextHeight || !editor) {
          return
        }

        const toastUiHeight = getToastUiEditorHeight(host, nextHeight)
        const markdownBody = host.querySelector('#markdown-body')
        if (markdownBody) {
          markdownBody.style.height = `${toastUiHeight}px`
        }

        if (typeof editor.setHeight === 'function') {
          editor.setHeight(toastUiHeight)
          return
        }

        const editorElement = host.querySelector('.toastui-editor-defaultUI')
          || host.querySelector('#markdown-body')
        if (editorElement) {
          editorElement.style.height = `${toastUiHeight}px`
        }
      },

      markClean: function () {
        // The application tracks dirty state through change notifications.
      },

      destroy: function () {
        clearHostEditorHeight(host)
        host.classList.remove('is-read-only')

        if (editor) {
          host.removeEventListener('pointerdown', handleModeSwitchIntent, true)
          host.removeEventListener('keydown', handleModeSwitchIntent, true)
          if (typeof editor.destroy === 'function') {
            editor.destroy()
          } else if (typeof editor.remove === 'function') {
            editor.remove()
          }
          editor = null
        }
        host.innerHTML = ''
      }
    }
  }

  window.Editor = {
    initialize: async function (options) {
      const editorOptions = options || {}
      activeMode = editorOptions.mode === 'markdown' ? 'markdown' : 'html'
      activeColorScheme = String(editorOptions.colorScheme || activeColorScheme).toUpperCase() === 'DARK'
        ? 'DARK'
        : 'LIGHT'
      pickImage = editorOptions.onPickImage || null

      const initializationId = ++activeInitialization
      destroyActiveAdapter()
      const adapter = activeMode === 'markdown'
        ? createToastUiAdapter(editorOptions)
        : createTinyMceAdapter(editorOptions)

      activeAdapter = adapter

      try {
        await adapter.initialize()
      } catch (error) {
        if (activeAdapter === adapter) {
          activeAdapter = null
        }

        throw error
      }

      if (initializationId !== activeInitialization) {
        if (activeAdapter === adapter) {
          adapter.destroy()
          activeAdapter = null
        }
      }
    },

    getMode: function () {
      return activeMode
    },

    load: function (content) {
      requireAdapter().load(content)
    },

    getHtml: function () {
      return requireAdapter().getHtml()
    },

    getMarkdown: function () {
      return requireAdapter().getMarkdown()
    },

    getMarkdownEditType: function () {
      return requireAdapter().getMarkdownEditType()
    },

    insertImage: function (src, attributes) {
      requireAdapter().insertImage(src, attributes)
    },

    insertLink: function (url, text) {
      requireAdapter().insertLink(url, text)
    },

    getSelectedText: function () {
      return requireAdapter().getSelectedText()
    },

    setSelectedText: function (text) {
      requireAdapter().setSelectedText(text)
    },

    execute: function (command, value) {
      requireAdapter().execute(command, value)
    },

    setReadOnly: function (readOnly) {
      requireAdapter().setReadOnly(readOnly)
    },

    setColorScheme: function (colorScheme) {
      activeColorScheme = String(colorScheme || '').toUpperCase() === 'DARK' ? 'DARK' : 'LIGHT'
      if (activeAdapter && typeof activeAdapter.setColorScheme === 'function') {
        activeAdapter.setColorScheme(activeColorScheme)
      }
    },

    setHeight: function (height) {
      requireAdapter().setHeight(height)
    },

    focus: function () {
      requireAdapter().focus()
    },

    markClean: function () {
      requireAdapter().markClean()
    },

    preloadHtml: function () {
      return ensureTinyMceLoaded()
    },

    renderMarkdown: renderMarkdown,

    onChanged: function (callback) {
      changedCallbacks.push(callback)
    },

    destroy: function () {
      activeInitialization += 1
      destroyActiveAdapter()
    }
  }
})(window, document)
