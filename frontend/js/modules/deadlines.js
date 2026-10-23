// VI: Trang Deadlines kiểu Notion Calendar: lịch tháng/tuần hiện task ngay trên ô ngày, kéo thả để đổi hạn,
//     bảng agenda bên phải theo ngày đang chọn, hộp chi tiết để sửa task.
// EN: Notion Calendar-style Deadlines view: month/week calendar showing tasks in day cells, drag and drop to
//     reschedule, an agenda panel for the selected day, and a details dialog to edit a task.

import { getTodos, addTodo, updateTodo, deleteTodo, sortTodos, getDueInfo, createTodoItem } from './todo.js';
import { todayKey } from '../utils/focusLog.js';
import { showToast } from '../utils/toast.js';
import { getSession } from '../utils/api.js';

const WEEKDAYS = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];
const MAX_CHIPS_PER_DAY = 3;
const HOUR_HEIGHT = 44; // px, must match --hour-height in CSS
const DAY_MS = 24 * 60 * 60 * 1000;

const FILTERS = {
    day: (todo) => todo.deadline === state.selectedDay,
    upcoming: (todo, due) => !todo.completed && (due.state === 'today' || due.state === 'soon' || due.state === 'later'),
    overdue: (todo, due) => due.state === 'overdue',
    nodate: (todo) => !todo.deadline && !todo.completed,
    done: (todo) => todo.completed,
};

const EMPTY_MESSAGES = {
    day: 'Nothing due on this day.',
    upcoming: 'No upcoming deadlines.',
    overdue: 'Nothing overdue. Great job!',
    nodate: 'Every task has a date.',
    done: 'No completed tasks yet.',
};

const state = {
    mode: 'month',
    filter: 'day',
    selectedDay: todayKey(),
    // VI: Ngày neo cho view đang hiển thị (tháng hoặc tuần). EN: Anchor date for the visible month or week.
    anchor: new Date(),
    editingId: null,
    weekScrolled: false,
};

function parseKey(key) {
    const [y, m, d] = key.split('-').map(Number);
    return new Date(y, m - 1, d);
}

function startOfWeek(date) {
    const d = new Date(date.getFullYear(), date.getMonth(), date.getDate());
    d.setDate(d.getDate() - ((d.getDay() + 6) % 7)); // Monday first
    return d;
}

function addDays(date, n) {
    const d = new Date(date);
    d.setDate(d.getDate() + n);
    return d;
}

function groupByDay(todos) {
    const byDay = {};
    todos.forEach(todo => {
        if (todo.deadline) (byDay[todo.deadline] ||= []).push(todo);
    });
    Object.values(byDay).forEach(list => list.sort((a, b) =>
        (a.completed - b.completed) || (a.time || '99:99').localeCompare(b.time || '99:99')));
    return byDay;
}

// ---------- Drag and drop ----------
function makeDraggable(el, todo) {
    el.draggable = true;
    el.addEventListener('dragstart', (e) => {
        e.dataTransfer.setData('text/plain', String(todo.id));
        e.dataTransfer.effectAllowed = 'move';
        el.classList.add('is-dragging');
        document.body.classList.add('is-dragging-task');
    });
    el.addEventListener('dragend', () => {
        el.classList.remove('is-dragging');
        document.body.classList.remove('is-dragging-task');
    });
}

function makeDropZone(el, getChanges) {
    el.addEventListener('dragover', (e) => {
        e.preventDefault();
        e.dataTransfer.dropEffect = 'move';
        el.classList.add('is-drop-target');
    });
    el.addEventListener('dragleave', () => el.classList.remove('is-drop-target'));
    el.addEventListener('drop', (e) => {
        e.preventDefault();
        el.classList.remove('is-drop-target');
        const id = e.dataTransfer.getData('text/plain');
        if (!id) return;
        const changes = getChanges(e);
        state.selectedDay = changes.deadline;
        updateTodo(id, changes);
        showToast(`Moved to ${parseKey(changes.deadline).toLocaleDateString('en-US', { weekday: 'short', month: 'short', day: 'numeric' })}${changes.time ? ' · ' + changes.time : ''}`, 'success', 2000);
    });
}

// ---------- Calendar chips ----------
function createChip(todo) {
    const due = getDueInfo(todo);
    const chip = document.createElement('button');
    chip.type = 'button';
    chip.className = `cal-chip priority-${todo.priority} due-${due.state} ${todo.completed ? 'completed' : ''}`;
    chip.title = `${todo.text}${todo.time ? ' · ' + todo.time : ''}`;

    if (todo.time) {
        const time = document.createElement('span');
        time.className = 'cal-chip-time';
        time.textContent = todo.time;
        chip.appendChild(time);
    }
    const text = document.createElement('span');
    text.className = 'cal-chip-text';
    text.textContent = todo.text;
    chip.appendChild(text);

    chip.addEventListener('click', (e) => {
        e.stopPropagation();
        openTaskModal(todo);
    });
    chip.addEventListener('dblclick', (e) => e.stopPropagation());
    makeDraggable(chip, todo);
    return chip;
}

// VI: Chỉ đổi class thay vì vẽ lại lịch, để double-click vẫn hoạt động trên cùng phần tử.
// EN: Only toggles classes instead of re-rendering, so a double-click still lands on the same element.
function selectDay(key) {
    state.selectedDay = key;
    state.filter = 'day';
    document.querySelectorAll('#calendar-grid [data-date]').forEach(el =>
        el.classList.toggle('is-selected', el.dataset.date === key));
    renderAgenda(getTodos());
}

// ---------- Month view ----------
function renderMonth(gridEl, byDay) {
    const anchor = state.anchor;
    const monthStart = new Date(anchor.getFullYear(), anchor.getMonth(), 1);
    const start = startOfWeek(monthStart);
    const today = todayKey();

    WEEKDAYS.forEach(name => {
        const head = document.createElement('div');
        head.className = 'calendar-weekday';
        head.textContent = name;
        gridEl.appendChild(head);
    });

    // VI: Hiện 5 hoặc 6 tuần tùy tháng. EN: Show 5 or 6 weeks depending on the month.
    const daysInMonth = new Date(anchor.getFullYear(), anchor.getMonth() + 1, 0).getDate();
    const weeks = Math.ceil((((monthStart.getDay() + 6) % 7) + daysInMonth) / 7);
    gridEl.style.setProperty('--weeks', weeks);

    for (let i = 0; i < weeks * 7; i++) {
        const date = addDays(start, i);
        const key = todayKey(date);
        const tasks = byDay[key] || [];

        const cell = document.createElement('div');
        cell.className = 'calendar-day';
        cell.dataset.date = key;
        if (date.getMonth() !== anchor.getMonth()) cell.classList.add('is-outside');
        if (key === today) cell.classList.add('is-today');
        if (key === state.selectedDay) cell.classList.add('is-selected');
        if (date.getDay() === 0 || date.getDay() === 6) cell.classList.add('is-weekend');

        const num = document.createElement('span');
        num.className = 'calendar-num';
        num.textContent = date.getDate() === 1 ? date.toLocaleDateString('en-US', { month: 'short', day: 'numeric' }) : date.getDate();
        cell.appendChild(num);

        const chips = document.createElement('div');
        chips.className = 'calendar-chips';
        tasks.slice(0, MAX_CHIPS_PER_DAY).forEach(todo => chips.appendChild(createChip(todo)));
        if (tasks.length > MAX_CHIPS_PER_DAY) {
            const more = document.createElement('button');
            more.type = 'button';
            more.className = 'cal-more';
            more.textContent = `+${tasks.length - MAX_CHIPS_PER_DAY} more`;
            more.addEventListener('click', (e) => {
                e.stopPropagation();
                selectDay(key);
            });
            chips.appendChild(more);
        }
        cell.appendChild(chips);

        // VI: Chấm nhỏ cho màn hình hẹp (khi chip bị ẩn). EN: Compact dots for narrow screens where chips are hidden.
        if (tasks.length) {
            const dots = document.createElement('span');
            dots.className = 'calendar-dots';
            tasks.slice(0, 4).forEach(todo => {
                const dot = document.createElement('i');
                dot.className = `due-${getDueInfo(todo).state}`;
                dots.appendChild(dot);
            });
            cell.appendChild(dots);
        }

        cell.addEventListener('click', () => selectDay(key));
        cell.addEventListener('dblclick', () => openTaskModal(null, { deadline: key }));
        makeDropZone(cell, () => ({ deadline: key }));
        gridEl.appendChild(cell);
    }
}

// ---------- Week view ----------
function renderWeek(gridEl, byDay) {
    const start = startOfWeek(state.anchor);
    const today = todayKey();
    const days = Array.from({ length: 7 }, (_, i) => addDays(start, i));

    // Header row
    gridEl.appendChild(Object.assign(document.createElement('div'), { className: 'week-corner' }));
    days.forEach(date => {
        const key = todayKey(date);
        const head = document.createElement('button');
        head.type = 'button';
        head.className = `week-day-head ${key === today ? 'is-today' : ''} ${key === state.selectedDay ? 'is-selected' : ''}`;
        head.dataset.date = key;
        head.innerHTML = '<span></span><b></b>';
        head.querySelector('span').textContent = WEEKDAYS[(date.getDay() + 6) % 7];
        head.querySelector('b').textContent = date.getDate();
        head.addEventListener('click', () => selectDay(key));
        gridEl.appendChild(head);
    });

    // All-day row: tasks without a time
    const allDayLabel = document.createElement('div');
    allDayLabel.className = 'week-allday-label';
    allDayLabel.textContent = 'All day';
    gridEl.appendChild(allDayLabel);
    days.forEach(date => {
        const key = todayKey(date);
        const cell = document.createElement('div');
        cell.className = 'week-allday';
        (byDay[key] || []).filter(todo => !todo.time).forEach(todo => cell.appendChild(createChip(todo)));
        cell.addEventListener('dblclick', () => openTaskModal(null, { deadline: key }));
        makeDropZone(cell, () => ({ deadline: key, time: '' }));
        gridEl.appendChild(cell);
    });

    // Hour grid
    const body = document.createElement('div');
    body.className = 'week-body';
    body.id = 'week-body';

    const hoursCol = document.createElement('div');
    hoursCol.className = 'week-hours';
    for (let h = 0; h < 24; h++) {
        const label = document.createElement('span');
        label.textContent = h === 0 ? '' : `${String(h).padStart(2, '0')}:00`;
        hoursCol.appendChild(label);
    }
    body.appendChild(hoursCol);

    days.forEach(date => {
        const key = todayKey(date);
        const col = document.createElement('div');
        col.className = `week-col ${key === today ? 'is-today' : ''}`;

        for (let h = 0; h < 24; h++) {
            const slot = document.createElement('div');
            slot.className = 'week-slot';
            const time = `${String(h).padStart(2, '0')}:00`;
            slot.addEventListener('dblclick', () => openTaskModal(null, { deadline: key, time }));
            makeDropZone(slot, () => ({ deadline: key, time }));
            col.appendChild(slot);
        }

        (byDay[key] || []).filter(todo => todo.time).forEach(todo => {
            const [h, m] = todo.time.split(':').map(Number);
            const chip = createChip(todo);
            chip.classList.add('week-event');
            chip.style.top = `${(h + m / 60) * HOUR_HEIGHT}px`;
            col.appendChild(chip);
        });

        if (key === today) {
            const now = new Date();
            const line = document.createElement('div');
            line.className = 'now-line';
            line.style.top = `${(now.getHours() + now.getMinutes() / 60) * HOUR_HEIGHT}px`;
            col.appendChild(line);
        }
        body.appendChild(col);
    });
    gridEl.appendChild(body);

    // VI: Lần đầu mở tuần thì cuộn tới 7 giờ sáng. EN: First time the week opens, scroll to 7 AM.
    if (!state.weekScrolled) {
        requestAnimationFrame(() => { body.scrollTop = 7 * HOUR_HEIGHT; });
        state.weekScrolled = true;
    }
}

function renderCalendar(todos) {
    const gridEl = document.getElementById('calendar-grid');
    const titleEl = document.getElementById('calendar-title');
    if (!gridEl) return;

    const prevScroll = document.getElementById('week-body')?.scrollTop;
    const byDay = groupByDay(todos);
    gridEl.innerHTML = '';
    gridEl.className = `calendar-grid ${state.mode}`;

    if (state.mode === 'month') {
        if (titleEl) titleEl.textContent = state.anchor.toLocaleDateString('en-US', { month: 'long', year: 'numeric' });
        renderMonth(gridEl, byDay);
    } else {
        const start = startOfWeek(state.anchor);
        const end = addDays(start, 6);
        const sameMonth = start.getMonth() === end.getMonth();
        if (titleEl) titleEl.textContent = sameMonth
            ? `${start.toLocaleDateString('en-US', { month: 'long' })} ${start.getDate()} – ${end.getDate()}, ${end.getFullYear()}`
            : `${start.toLocaleDateString('en-US', { month: 'short', day: 'numeric' })} – ${end.toLocaleDateString('en-US', { month: 'short', day: 'numeric' })}, ${end.getFullYear()}`;
        renderWeek(gridEl, byDay);
        if (prevScroll !== undefined) document.getElementById('week-body').scrollTop = prevScroll;
    }

    document.querySelectorAll('#calendar-mode button').forEach(btn =>
        btn.classList.toggle('active', btn.dataset.mode === state.mode));
}

// ---------- Agenda panel ----------
function renderAgenda(todos) {
    const listEl = document.getElementById('deadline-list');
    const titleEl = document.getElementById('agenda-title');
    const eyebrowEl = document.getElementById('agenda-eyebrow');
    const textInput = document.getElementById('deadline-text');
    if (!listEl) return;

    const day = parseKey(state.selectedDay);
    const diff = Math.round((day - parseKey(todayKey())) / DAY_MS);
    if (eyebrowEl) eyebrowEl.textContent = diff === 0 ? 'Today' : diff === 1 ? 'Tomorrow' : diff === -1 ? 'Yesterday' : day.toLocaleDateString('en-US', { weekday: 'long' });
    if (titleEl) titleEl.textContent = day.toLocaleDateString('en-US', { weekday: diff >= -1 && diff <= 1 ? 'long' : undefined, month: 'long', day: 'numeric' });
    if (textInput) textInput.placeholder = `Add a task for ${day.toLocaleDateString('en-US', { month: 'short', day: 'numeric' })}...`;

    const withDue = todos.map(todo => ({ todo, due: getDueInfo(todo) }));

    // VI: Cập nhật số đếm trên các chip lọc. EN: Update the counters on filter chips.
    document.querySelectorAll('#deadline-filters .chip').forEach(chip => {
        const filter = chip.dataset.filter;
        const count = withDue.filter(({ todo, due }) => FILTERS[filter](todo, due)).length;
        chip.classList.toggle('active', filter === state.filter);
        const countEl = chip.querySelector('.chip-count');
        if (countEl) countEl.textContent = count;
    });

    const visible = withDue.filter(({ todo, due }) => FILTERS[state.filter](todo, due)).map(({ todo }) => todo);

    listEl.innerHTML = '';
    if (visible.length === 0) {
        const empty = document.createElement('li');
        empty.className = 'empty-state';
        empty.innerHTML = '<i class="ph ph-calendar-check"></i><span></span>';
        empty.querySelector('span').textContent = EMPTY_MESSAGES[state.filter];
        listEl.appendChild(empty);
        return;
    }
    sortTodos(visible).forEach(todo => {
        const item = createTodoItem(todo);
        item.querySelector('.todo-body').addEventListener('click', () => openTaskModal(todo));
        makeDraggable(item, todo);
        listEl.appendChild(item);
    });
}

export function renderDeadlines() {
    const todos = getTodos();
    renderCalendar(todos);
    renderAgenda(todos);
}

// ---------- Task details dialog ----------
function openTaskModal(todo, defaults = {}) {
    const modal = document.getElementById('task-modal');
    if (!modal) return;
    state.editingId = todo ? todo.id : null;

    const data = todo || { text: '', deadline: state.selectedDay, time: '', priority: 'medium', completed: false, ...defaults };
    document.getElementById('task-modal-title').lastChild.textContent = todo ? ' Edit task' : ' New task';
    document.getElementById('task-text').value = data.text;
    document.getElementById('task-date').value = data.deadline || '';
    document.getElementById('task-time').value = data.time || '';
    document.getElementById('task-priority').value = data.priority;
    document.getElementById('task-done').checked = !!data.completed;
    document.getElementById('task-delete-btn').hidden = !todo;

    modal.dataset.openedAt = Date.now();
    modal.classList.remove('hidden');
    document.getElementById('task-text').focus();
}

function closeTaskModal() {
    document.getElementById('task-modal')?.classList.add('hidden');
    state.editingId = null;
}

function initTaskModal() {
    const form = document.getElementById('task-form');
    if (!form) return;

    form.addEventListener('submit', (e) => {
        e.preventDefault();
        const text = document.getElementById('task-text').value.trim();
        if (!text) return;
        const changes = {
            text,
            deadline: document.getElementById('task-date').value || null,
            time: document.getElementById('task-time').value,
            priority: document.getElementById('task-priority').value,
            completed: document.getElementById('task-done').checked,
        };
        if (state.editingId) updateTodo(state.editingId, changes);
        else addTodo(changes);
        if (changes.deadline) state.selectedDay = changes.deadline;
        closeTaskModal();
        renderDeadlines();
        showToast('Task saved.', 'success', 2000);
    });

    document.getElementById('task-close-btn').addEventListener('click', closeTaskModal);
    document.getElementById('task-delete-btn').addEventListener('click', () => {
        if (state.editingId && deleteTodo(state.editingId)) closeTaskModal();
    });
}

// VI: Nhắc một lần khi mở app nếu có task quá hạn hoặc đến hạn hôm nay.
// EN: One-time reminder on load when tasks are overdue or due today.
export function remindDueTasks() {
    const pending = getTodos().filter(todo => !todo.completed);
    const overdue = pending.filter(todo => getDueInfo(todo).state === 'overdue').length;
    const dueToday = pending.filter(todo => getDueInfo(todo).state === 'today').length;
    if (overdue) showToast(`You have ${overdue} overdue task${overdue > 1 ? 's' : ''}.`, 'error', 5000);
    else if (dueToday) showToast(`${dueToday} task${dueToday > 1 ? 's' : ''} due today.`, 'info', 5000);
}

export function initDeadlines() {
    const form = document.getElementById('deadline-form');
    const textInput = document.getElementById('deadline-text');
    const timeInput = document.getElementById('deadline-time');
    const priorityInput = document.getElementById('deadline-priority');

    if (form) {
        form.addEventListener('submit', (e) => {
            e.preventDefault();
            const text = textInput.value.trim();
            if (!text) return;
            addTodo({ text, deadline: state.selectedDay, time: timeInput.value, priority: priorityInput.value });
            textInput.value = '';
            timeInput.value = '';
            textInput.focus();
        });
    }

    document.querySelectorAll('#deadline-filters .chip').forEach(chip => {
        chip.addEventListener('click', () => {
            state.filter = chip.dataset.filter;
            renderAgenda(getTodos());
        });
    });

    document.querySelectorAll('#calendar-mode button').forEach(btn => {
        btn.addEventListener('click', () => {
            state.mode = btn.dataset.mode;
            state.anchor = parseKey(state.selectedDay);
            renderDeadlines();
        });
    });

    const shift = (delta) => {
        const a = state.anchor;
        state.anchor = state.mode === 'month'
            ? new Date(a.getFullYear(), a.getMonth() + delta, 1)
            : addDays(a, delta * 7);
        renderCalendar(getTodos());
    };
    document.getElementById('calendar-prev')?.addEventListener('click', () => shift(-1));
    document.getElementById('calendar-next')?.addEventListener('click', () => shift(1));
    document.getElementById('calendar-today')?.addEventListener('click', () => {
        state.anchor = new Date();
        state.selectedDay = todayKey();
        state.filter = 'day';
        renderDeadlines();
    });
    document.getElementById('new-task-btn')?.addEventListener('click', () => openTaskModal(null));

    // VI: Phím tắt trong trang Deadlines: N tạo task, ←/→ chuyển tháng/tuần, T về hôm nay.
    // EN: Deadlines shortcuts: N new task, ←/→ previous/next, T today.
    document.addEventListener('keydown', (e) => {
        const view = document.getElementById('view-deadlines');
        if (!view?.classList.contains('active') || e.ctrlKey || e.metaKey || e.altKey) return;
        if (e.target.closest('input, select, textarea') || document.querySelector('.modal-overlay:not(.hidden):not(.auth-modal-overlay), .auth-modal-overlay.active')) return;
        if (e.key === 'n' || e.key === 'N') { e.preventDefault(); openTaskModal(null); }
        if (e.key === 'ArrowLeft') shift(-1);
        if (e.key === 'ArrowRight') shift(1);
        if (e.key === 't' || e.key === 'T') document.getElementById('calendar-today').click();
    });

    initTaskModal();
    window.addEventListener('studyhub:todos', renderDeadlines);
    renderDeadlines();
    // VI: Khi có phiên đăng nhập, nhắc sau khi tải task từ server (xem sync.js).
    // EN: With a session, the reminder runs after tasks load from the server (see sync.js).
    if (!getSession()) remindDueTasks();
}
