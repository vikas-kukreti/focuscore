package com.focuscore.viewmodel

import android.app.Application
import android.content.SharedPreferences
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.mutableStateOf
import androidx.lifecycle.mutableStateFlow
import androidx.lifecycle.asStateFlow
import com.focuscore.FocusCoreClient
import com.focuscore.TimeboxReminderReceiver
import com.focuscore.model.Task
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.launch

class TodayViewModel(application: Application) : AndroidViewModel(application) {

    private val prefs: SharedPreferences =
        getApplication().getSharedPreferences("focuscore", android.content.Context.MODE_PRIVATE)
    private var nextTaskId = System.currentTimeMillis()

    private val _tasks = mutableStateOf(listOf<Task>())
    val tasks: androidx.compose.runtime.State<List<Task>>
        get() = _tasks

    init {
        loadTasks()
    }

    private fun loadTasks() {
        // TODO: replace with the native task list from libfocuscore (generated UniFFI bindings).
        _tasks.value = listOf(
            Task(
                id = nextId(),
                title = "Review quarterly goals",
                notes = "Prepare Q4 deck and align with product."
            ),
            Task(
                id = nextId(),
                title = "Write weekly report",
                notes = "Include focus time metrics from libfocuscore timers."
            )
        )
    }

    private fun nextId(): Long {
        nextTaskId += 1
        return nextTaskId
    }

    fun quickAdd(title: String) {
        if (title.isBlank()) return
        val task = Task(id = nextId(), title = title)
        // Native: persisted by libfocuscore via generated bindings.
        FocusCoreClient.quickAdd(title)
        _tasks.value = _tasks.value + listOf(task)
    }

    fun toggleDone(task: Task) {
        _tasks.value = _tasks.value.map { if (it.id == task.id) it.copy(done = !it.done) else it }
    }

    /** Schedules an exact-alarm reminder for the timebox block and tags the task. */
    fun schedule(taskId: Long, title: String, startAtMillis: Long, minutes: Int) {
        com.focuscore.TimeboxReminderReceiver.scheduleTimeboxReminder(
            getApplication(), taskId, title, startAtMillis
        )
        _tasks.value = _tasks.value.map { task ->
            if (task.id == taskId) task.copy(timeboxAt = startAtMillis, timeboxMinutes = minutes) else task
        }
    }
}