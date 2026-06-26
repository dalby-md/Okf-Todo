(function ($) {
  const storageKey = 'okf-todo.items'

  function readItems() {
    const savedItems = window.localStorage.getItem(storageKey)

    if (!savedItems) {
      return []
    }

    try {
      return JSON.parse(savedItems)
    } catch {
      return []
    }
  }

  function writeItems(items) {
    window.localStorage.setItem(storageKey, JSON.stringify(items))
  }

  function createId() {
    if (window.crypto && window.crypto.randomUUID) {
      return window.crypto.randomUUID()
    }

    return `${Date.now()}-${Math.random().toString(16).slice(2)}`
  }

  function createTodoItem(item) {
    const $checkbox = $('<input>', {
      type: 'checkbox',
      checked: item.done,
      'aria-label': `Mark ${item.text} complete`
    })

    const $label = $('<span>', {
      class: 'todo-text',
      text: item.text
    })

    const $deleteButton = $('<button>', {
      class: 'icon-button',
      type: 'button',
      text: 'Delete',
      'aria-label': `Delete ${item.text}`
    })

    return $('<li>', {
      class: item.done ? 'todo-item is-done' : 'todo-item',
      'data-id': item.id
    }).append($checkbox, $label, $deleteButton)
  }

  function render(items) {
    const $list = $('#todo-list')
    $list.empty()

    if (items.length === 0) {
      $('#empty-state').show()
      return
    }

    $('#empty-state').hide()
    items.forEach((item) => $list.append(createTodoItem(item)))
  }

  $(function () {
    let items = readItems()

    $('#app').html(`
      <main class="app-shell">
        <section class="todo-panel" aria-labelledby="app-title">
          <header class="app-header">
            <div>
              <p class="eyebrow">Photino + jQuery</p>
              <h1 id="app-title">OKF Todo</h1>
            </div>
            <span id="todo-count" class="todo-count"></span>
          </header>

          <form id="todo-form" class="todo-form">
            <input
              id="todo-input"
              name="todo"
              type="text"
              autocomplete="off"
              placeholder="Add a task"
              aria-label="Todo text"
            />
            <button type="submit">Add</button>
          </form>

          <p id="empty-state" class="empty-state">No tasks yet.</p>
          <ul id="todo-list" class="todo-list" aria-label="Todo items"></ul>
        </section>
      </main>
    `)

    function update() {
      writeItems(items)
      render(items)
      const openCount = items.filter((item) => !item.done).length
      $('#todo-count').text(`${openCount} open`)
    }

    $('#todo-form').on('submit', function (event) {
      event.preventDefault()

      const $input = $('#todo-input')
      const text = $input.val().toString().trim()

      if (!text) {
        $input.trigger('focus')
        return
      }

      items = [
        ...items,
        {
          id: createId(),
          text,
          done: false
        }
      ]

      $input.val('').trigger('focus')
      update()
    })

    $('#todo-list').on('change', 'input[type="checkbox"]', function () {
      const id = $(this).closest('.todo-item').data('id')

      items = items.map((item) =>
        item.id === id
          ? {
              ...item,
              done: this.checked
            }
          : item
      )

      update()
    })

    $('#todo-list').on('click', 'button', function () {
      const id = $(this).closest('.todo-item').data('id')
      items = items.filter((item) => item.id !== id)
      update()
    })

    update()
  })
})(jQuery)
