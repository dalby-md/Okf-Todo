(function (window, document) {
  const changedCallbacks = []
  const defaultSelector = '#text-body'

  let activeAdapter = null
  let activeMode = 'html'
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
      script.src = `${src}?v=${Date.now()}`
      script.onload = resolve
      script.onerror = function () {
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

  function destroyActiveAdapter() {
    if (!activeAdapter) {
      return
    }

    activeAdapter.destroy()
    activeAdapter = null
  }

  function createTinyMceAdapter(options) {
    let editor = null
    const selector = options.selector || defaultSelector
    const elementId = getElementId(selector)
    const host = getHost(options)

    host.innerHTML = `<textarea id="${encodeAttribute(elementId)}"></textarea>`
    document.querySelector(selector).value = options.initialContent || ''

    return {
      initialize: async function () {
        if (!window.tinymce) {
          throw new Error('TinyMCE did not load.')
        }

        await window.tinymce.init({
          selector,
          base_url: options.baseUrl || '/tinymce',
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
          file_picker_callback: pickAndInsertImage,
          images_upload_handler: async function (blobInfo) {
            if (typeof pickImage !== 'function') {
              throw new Error('Image upload is not configured.')
            }

            const image = await pickImage(blobInfo.blob())
            return image.src
          },
          min_height: options.minHeight || 420,
          autoresize_bottom_margin: 0,
          content_style: options.contentStyle || '',
          setup: function (tinyEditor) {
            editor = tinyEditor
            tinyEditor.on('change keyup undo redo setcontent', notifyChanged)
          }
        })

        editor = window.tinymce.get(elementId)
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

      insertImage: function (src, attributes) {
        const imageAttributes = Object.assign({}, attributes || {}, { src })
        const attributeHtml = toAttributeHtml(imageAttributes)
        editor.insertContent(`<img ${attributeHtml}>`)
      },

      insertLink: function (url, text) {
        const selectedText = text || editor.selection.getContent({ format: 'text' }) || url
        editor.insertContent(`<a href="${encodeAttribute(url)}">${encodeText(selectedText)}</a>`)
      },

      getSelectedText: function () {
        return editor.selection.getContent({ format: 'text' })
      },

      setSelectedText: function (text) {
        editor.insertContent(encodeText(text || ''))
      },

      execute: function (command, value) {
        editor.execCommand(command, false, value)
      },

      setReadOnly: function (readOnly) {
        editor.mode.set(readOnly ? 'readonly' : 'design')
      },

      focus: function () {
        editor.focus()
      },

      markClean: function () {
        editor.setDirty(false)
      },

      destroy: function () {
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
    const host = getHost(options)

    host.innerHTML = `
      <div class="markdown-toolbar" aria-label="Markdown formatting">
        <button type="button" data-markdown-command="undo">Undo</button>
        <button type="button" data-markdown-command="redo">Redo</button>
        <button type="button" data-markdown-command="heading">H</button>
        <button type="button" data-markdown-command="bold">B</button>
        <button type="button" data-markdown-command="italic">I</button>
        <button type="button" data-markdown-command="ul">List</button>
        <button type="button" data-markdown-command="ol">1.</button>
        <button type="button" data-markdown-command="quote">Quote</button>
        <button type="button" data-markdown-command="link">Link</button>
        <button type="button" data-markdown-command="image">Image</button>
        <button type="button" data-markdown-command="table">Table</button>
        <button type="button" data-markdown-command="code">Code</button>
      </div>
      <div id="markdown-body"></div>
    `

    function bindToolbar() {
      host.querySelectorAll('[data-markdown-command]').forEach(function (button) {
        button.addEventListener('click', async function () {
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

        editor = new window.toastui.Editor({
          el: document.querySelector('#markdown-body'),
          height: `${options.minHeight || 420}px`,
          initialEditType: 'markdown',
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

        if (options.initialHtml) {
          editor.setHtml(options.initialHtml, false)
        }

        editor.on('change', notifyChanged)
        bindToolbar()
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

      insertImage: function (src, attributes) {
        const alt = escapeMarkdown((attributes && attributes.alt) || 'image')
        editor.insertText(`![${alt}](${src})`)
      },

      insertLink: function (url, text) {
        const selectedText = text || this.getSelectedText() || url
        editor.insertText(`[${escapeMarkdown(selectedText)}](${url})`)
      },

      getSelectedText: function () {
        return ''
      },

      setSelectedText: function (text) {
        editor.insertText(text || '')
      },

      execute: function (command, value) {
        if (command === 'Bold') {
          this.setSelectedText(`**${this.getSelectedText() || value || ''}**`)
          return
        }

        if (command === 'Italic') {
          this.setSelectedText(`*${this.getSelectedText() || value || ''}*`)
        }
      },

      setReadOnly: function () {
        // TOAST UI v2 does not expose a stable read-only API in the browser bundle.
      },

      focus: function () {
        editor.focus()
      },

      markClean: function () {
        // The application tracks dirty state through change notifications.
      },

      destroy: function () {
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

  window.Editor = {
    initialize: async function (options) {
      const editorOptions = options || {}
      activeMode = editorOptions.mode === 'markdown' ? 'markdown' : 'html'
      pickImage = editorOptions.onPickImage || null

      destroyActiveAdapter()
      activeAdapter = activeMode === 'markdown'
        ? createToastUiAdapter(editorOptions)
        : createTinyMceAdapter(editorOptions)

      await activeAdapter.initialize()
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

    focus: function () {
      requireAdapter().focus()
    },

    markClean: function () {
      requireAdapter().markClean()
    },

    onChanged: function (callback) {
      changedCallbacks.push(callback)
    }
  }
})(window, document)
