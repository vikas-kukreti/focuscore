package com.focuscore

import android.app.AlarmManager
import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.os.Build
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat

/**
 * Broadcast receiver for exact timebox reminders.
 *
 * AlarmManager.setExactAndAllowWhileIdle (requires SCHEDULE_EXACT_ALARM) fires
 * when a scheduled daily timebox block begins; this receiver posts the
 * timebox_reminder notification.
 */
class TimeboxReminderReceiver : BroadcastReceiver() {

    override fun onReceive(context: Context, intent: Intent?) {
        NotificationChannels.ensureCreated(context)
        val taskId = intent?.getLongExtra(FocusService.EXTRA_TASK_ID, 0) ?: 0
        val title = intent?.getStringExtra(FocusService.EXTRA_TASK_TITLE)
            ?: context.getString(R.string.timebox_reminder_title)

        val openIntent = Intent(context, MainActivity::class.java).apply {
            putExtra(FocusService.EXTRA_TASK_ID, taskId)
        }
        val contentIntent = PendingIntent.getActivity(
            context,
            taskId.toInt(),
            openIntent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )

        val notification = NotificationCompat.Builder(context, FocusService.CHANNEL_TIMEBOX)
            .setSmallIcon(android.R.drawable.ic_dialog_info)
            .setContentTitle(context.getString(R.string.timebox_reminder_title))
            .setContentText(context.getString(R.string.timebox_reminder_body, title))
            .setAutoCancel(true)
            .setPriority(NotificationCompat.PRIORITY_DEFAULT)
            .setContentIntent(contentIntent)
            .build()

        NotificationManagerCompat.from(context).notify(1000 + taskId.toInt(), notification)
    }

    companion object {
        private const val REQUEST_CODE_OFFSET = 10_000

        /**
         * Schedules an exact, idle-allowing alarm for a timebox block start.
         * Requires android.permission.SCHEDULE_EXACT_ALARM (granted at install,
         * targetSdk 34).
         */
        fun scheduleTimeboxReminder(context: Context, taskId: Long, title: String, startAtMillis: Long) {
            cancelTimeboxReminder(context, taskId)
            val alarmManager = context.getSystemService(Context.ALARM_SERVICE) as AlarmManager
            val intent = Intent(context, TimeboxReminderReceiver::class.java).apply {
                putExtra(FocusService.EXTRA_TASK_ID, taskId)
                putExtra(FocusService.EXTRA_TASK_TITLE, title)
            }
            val pending = PendingIntent.getBroadcast(
                context,
                (REQUEST_CODE_OFFSET + taskId).toInt(),
                intent,
                PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
            )
            alarmManager.setExactAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, startAtMillis, pending)
        }

        fun cancelTimeboxReminder(context: Context, taskId: Long) {
            val alarmManager = context.getSystemService(Context.ALARM_SERVICE) as AlarmManager
            val intent = Intent(context, TimeboxReminderReceiver::class.java)
            val pending = PendingIntent.getBroadcast(
                context,
                (REQUEST_CODE_OFFSET + taskId).toInt(),
                intent,
                PendingIntent.FLAG_NO_CREATE or PendingIntent.FLAG_IMMUTABLE
            )
            pending?.let { alarmManager.cancel(it) }
        }
    }
}
