package com.focuscore.viewmodel

import android.app.Application
import android.content.Context
import android.content.Intent
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import androidx.lifecycle.mutableStateOf
import androidx.lifecycle.mutableStateOf
import com.focuscore.FocusCoreClient
import com.focuscore.FocusService
import com.focuscore.model.Task
import com.focuscore.model.TimerUiState
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.launch
import java.util.concurrent.TimeUnit

class PomodoroViewModel(application: Application) : AndroidViewModel(application) {

    private val prefs: android.content.SharedPreferences =
        getApplication().getSharedPreferences("focuscore", Context.MODE_PRIVATE)

    private val _uiState = mutableStateOf(TimerUiState())
    val uiState: androidx.compose.runtime.State<TimerUiState>
        get() = _uiState

    init {
        viewModelScope.launch {
            pollTimer()
        }
    }

    /** Polls the native timer every second and republishes UI state. */
    private suspend fun pollTimer() {
        while (true) {
            delay(1000)
            _uiState.value = currentUiState()
        }
    }

    private fun currentUiState(): TimerUiState {
        val startedAt = prefs.getLong("timer_started_at", 0L)
        val total = prefs.getInt("timer_total_seconds", 25 * 60)
        val running = prefs.getBoolean("timer_running", false)
        val elapsed = if (startedAt > 0) {
            TimeUnit.MILLISECONDS.toSeconds(System.currentTimeMillis() - startedAt).coerceAtLeast(0)
        } else {
            0L
        }
        val remaining = (total - elapsed).coerceAtLeast(0)
        return TimerUiState(
            isRunning = running,
            taskId = prefs.getLong("current_task_id", 0),
            taskTitle = prefs.getString("current_task_title", "") ?: "",
            elapsedSeconds = elapsed,
            totalSeconds = total.toLong(),
            remainingSeconds = remaining
        )
    }

    fun start(task: Task, totalSeconds: Int = task.pomodoroTotalSeconds) {
        prefs.edit()
            .putBoolean("timer_running", true)
            .putLong("current_task_id", task.id)
            .putString("current_task_title", task.title)
            .putLong("timer_started_at", System.currentTimeMillis())
            .putInt("timer_total_seconds", totalSeconds)
            .apply()

        // Delegate to native timer (libfocuscore via generated UniFFI bindings).
        FocusCoreClient.timerStart(task.id, totalSeconds)

        // Promote to an ongoing foreground service so the timer survives the background.
        startForegroundService(task.title, totalSeconds)
    }

    fun pause() {
        prefs.edit().putBoolean("timer_running", false).apply()
        FocusCoreClient.timerPause()
        updateForegroundService(paused = true)
    }

    fun stop() {
        prefs.edit()
            .putBoolean("timer_running", false)
            .remove("current_task_id")
            .remove("current_task_title")
            .remove("timer_started_at")
            .apply()

        FocusCoreClient.timerStop()
        stopForegroundService()
    }

    private fun startForegroundService(title: String, totalSeconds: Int) {
        val context = getApplication()
        val intent = Intent(context, FocusService::class.java).apply {
            action = FocusService.ACTION_CMD_START
            putExtra(FocusService.EXTRA_TASK_TITLE, title)
            putExtra(FocusService.EXTRA_TOTAL_SECONDS, totalSeconds)
        }
        if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.O) {
            context.startForegroundService(intent)
        } else {
            context.startService(intent)
        }
    }

    private fun updateForegroundService(paused: Boolean) {
        val context = getApplication()
        val intent = Intent(context, FocusService::class.java).apply {
            action = if (paused) FocusService.ACTION_CMD_PAUSE else FocusService.ACTION_CMD_START
        }
        if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.O) {
            context.startForegroundService(intent)
        } else {
            context.startService(intent)
        }
    }

    private fun stopForegroundService() {
        val context = getApplication()
        val intent = Intent(context, FocusService::class.java).apply {
            action = FocusService.ACTION_CMD_STOP
        }
        if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.O) {
            context.startForegroundService(intent)
        } else {
            context.startService(intent)
        }
    }
}