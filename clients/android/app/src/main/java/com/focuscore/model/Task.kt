package com.focuscore.model

data class Task(
    val id: Long = 0,
    val title: String,
    val notes: String = "",
    val done: Boolean = false,
    val createdAt: Long = System.currentTimeMillis(),
    /** Epoch millis when the scheduled timebox block starts (null if unscheduled). */
    val timeboxAt: Long? = null,
    val timeboxMinutes: Int = 0,
    val pomodoroTotalSeconds: Int = 25 * 60
)

data class TimerUiState(
    val isRunning: Boolean = false,
    val taskId: Long = 0,
    val taskTitle: String = "",
    val elapsedSeconds: Long = 0,
    val totalSeconds: Long = 25 * 60,
    val remainingSeconds: Long = 25 * 60
)

data class TimeboxSlot(
    val startMillis: Long,
    val taskId: Long = 0,
    val taskTitle: String = ""
)
