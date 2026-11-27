package com.focuscore.viewmodel

import android.app.Application
import android.content.SharedPreferences
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.MutableStateFlow
import androidx.lifecycle.asStateFlow
import com.focuscore.TimeboxReminderReceiver
import com.focuscore.model.TimeboxSlot
import kotlinx.coroutines.flow.StateFlow
import java.util.Calendar

class TimeboxViewModel(application: Application) : AndroidViewModel(application) {

    private val prefs: SharedPreferences =
        getApplication().getSharedPreferences("focuscore", android.content.Context.MODE_PRIVATE)

    private val _slots = MutableStateFlow(buildSlots())
    val slots: StateFlow<List<TimeboxSlot>> = _slots.asStateFlow()

    /** Half-hour slots from 06:00 to 23:00 today, with assigned tasks restored from prefs. */
    private fun buildSlots(): List<TimeboxSlot> {
        val slots = mutableListOf<TimeboxSlot>()
        val cal = Calendar.getInstance().apply {
            set(Calendar.HOUR_OF_DAY, 6)
            set(Calendar.MINUTE, 0)
            set(Calendar.SECOND, 0)
            set(Calendar.MILLISECOND, 0)
        }
        val end = Calendar.getInstance().apply {
            set(Calendar.HOUR_OF_DAY, 23)
            set(Calendar.MINUTE, 0)
        }
        while (cal.timeInMillis <= end.timeInMillis) {
            val start = cal.timeInMillis
            slots.add(TimeboxSlot(
                startMillis = start,
                taskId = prefs.getLong("timebox_id_$start", 0L),
                taskTitle = prefs.getString("timebox_title_$start", "") ?: ""
            ))
            cal.add(Calendar.MINUTE, 30)
        }
        return slots
    }

    fun refresh() {
        _slots.value = buildSlots()
    }

    /** Assigns a task to a slot and schedules an exact-alarm timebox reminder. */
    fun assign(title: String, slot: TimeboxSlot) {
        prefs.edit()
            .putLong("timebox_id_${slot.startMillis}", slot.startMillis)
            .putString("timebox_title_${slot.startMillis}", title)
            .apply()
        TimeboxReminderReceiver.scheduleTimeboxReminder(
            getApplication(), slot.startMillis, title, slot.startMillis
        )
        refresh()
    }

    fun unassign(slot: TimeboxSlot) {
        TimeboxReminderReceiver.cancelTimeboxReminder(getApplication(), slot.startMillis)
        prefs.edit()
            .remove("timebox_id_${slot.startMillis}")
            .remove("timebox_title_${slot.startMillis}")
            .apply()
        refresh()
    }
}