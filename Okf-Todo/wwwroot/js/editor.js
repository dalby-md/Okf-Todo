(function (window) {
  const changedCallbacks = []
  let activeEditor = null
  let pickImage = null

  function requireEditor() {
    if (!activeEditor) {
      throw new Error('Editor is not initialized.')
    }

    return activeEditor
  }

  function notifyChanged() {
    changedCallbacks.forEach(function (callback) {
      callback()
    })
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

  window.Editor = {
    initialize: async function (options) {
      const editorOptions = options || {}

      if (!window.tinymce) {
        throw new Error('TinyMCE did not load.')
      }

      pickImage = editorOptions.onPickImage || null

      await window.tinymce.init({
        selector: editorOptions.selector,
        base_url: editorOptions.baseUrl || '/tinymce',
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
        min_height: editorOptions.minHeight || 420,
        autoresize_bottom_margin: 0,
        content_style: editorOptions.contentStyle || '',
        setup: function (editor) {
          activeEditor = editor
          editor.on('change keyup undo redo setcontent', notifyChanged)
        }
      })

      activeEditor = window.tinymce.get(editorOptions.selector.replace(/^#/, ''))
    },

    load: function (html) {
      requireEditor().setContent(html || '')
    },

    getHtml: function () {
      return requireEditor().getContent({ format: 'html' })
    },

    insertImage: function (src, attributes) {
      const imageAttributes = Object.assign({}, attributes || {}, { src })
      const attributeHtml = toAttributeHtml(imageAttributes)
      requireEditor().insertContent(`<img ${attributeHtml}>`)
    },

    insertLink: function (url, text) {
      const editor = requireEditor()
      const selectedText = text || editor.selection.getContent({ format: 'text' }) || url
      editor.insertContent(`<a href="${encodeAttribute(url)}">${encodeText(selectedText)}</a>`)
    },

    getSelectedText: function () {
      return requireEditor().selection.getContent({ format: 'text' })
    },

    setSelectedText: function (text) {
      requireEditor().insertContent(encodeText(text || ''))
    },

    execute: function (command, value) {
      requireEditor().execCommand(command, false, value)
    },

    setReadOnly: function (readOnly) {
      requireEditor().mode.set(readOnly ? 'readonly' : 'design')
    },

    focus: function () {
      requireEditor().focus()
    },

    markClean: function () {
      requireEditor().setDirty(false)
    },

    onChanged: function (callback) {
      changedCallbacks.push(callback)
    }
  }
})(window)
