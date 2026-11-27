package com.focuscore

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.os.Build
import android.os.IBinder
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import androidx.core.content.ContextCompat
import com.focuscore.core.FocusCore

class FocusService : Service() {

    companion object {
        const val ACTION_CMD_START = "com.focuscore.action.CMD_START"
        const val ACTION_CMD_PAUSE = "com.focuscore.action.CMD_PAUSE"
        const val ACTION_CMD_STOP = "com.focuscore.action.CMD_STOP"
        const val ACTION_CMD_DONE = "com.focuscore.action.CMD_DONE"

        const val EXTRA_TASK_ID = "com.focuscore.extra.TASK_ID"
        const val EXTRA_TASK_TITLE = "com.focuscore.extra.TASK_TITLE"
        const val EXTRA_TOTAL_SECONDS = "com.focuscore.extra.TOTAL_SECONDS"

        const val NOTIFICATION_ID = 1
        const val CHANNEL_FOREGROUND = "focuscore_foreground"
        const val CHANNEL_TIMEBOX = "focuscore_timebox"
    }

    // TODO: replace with generated UniFFI bindings (com.focuscore.core.FocusCore)
    private val nativeHandle: Long =
        try { FocusCore.focuscoreOpen() } catch (_: UnsatisfiedLinkError) { 0L }

    override fun onCreate() {
        super.onCreate()
        NotificationChannels.ensureCreated(this)
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        when (intent?.action) {
            ACTION_CMD_START -> {
                val title = intent.getStringExtra(EXTRA_TASK_TITLE) ?: getString(R.string.app_name)
                val totalSeconds = intent.getIntExtra(EXTRA_TOTAL_SECONDS, 25 * 60)
                try {
                    FocusCore.focuscoreTimerStart(
                        nativeHandle,
                        intent.getLongExtra(EXTRA_TASK_ID, 0),
                        totalSeconds
                    )
                } catch (_: UnsatisfiedLinkError) {
                    // libfocuscore.so is bundled by CI (cargo-ndk); absent in local builds.
                }
                showForegroundNotification(title, totalSeconds, running = true)
            }
            ACTION_CMD_PAUSE -> {
                try { FocusCore.focuscoreTimerPause(nativeHandle) } catch (_: UnsatisfiedLinkError) {}
                showForegroundNotification(getString(R.string.app_name), 0, running = false)
            }
            ACTION_CMD_STOP, ACTION_CMD_DONE -> {
                try { FocusCore.focuscoreTimerStop(nativeHandle) } catch (_: UnsatisfiedLinkError) {}
                stopForeground(true)
                stopSelf()
            }
        }
        return START_NOT_STICKY
    }

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onDestroy() {
        stopForeground(true)
        super.onDestroy()
    }

    /**
     * Ongoing high-priority notification with a chronometer countdown, the active
     * task title, and inline Pause / Stop / Done actions bound to this service.
     */
    private fun showForegroundNotification(title: String, totalSeconds: Int, running: Boolean) {
        val channelId = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) CHANNEL_FOREGROUND else ""
        val builder = NotificationCompat.Builder(this, channelId)
            .setSmallIcon(R.drawable.ic_notification)
            .setContentTitle(title)
            .setContentText(if (running) "Timer running" else "Timer paused")
            .setOngoing(true)
            .setOnlyAlertOnce(true)
            .setCategory(NotificationCompat.CATEGORY_SERVICE)
            .setPriority(NotificationCompat.PRIORITY_HIGH)

        if (running && totalSeconds > 0) {
            // Chronometer counts down from totalSeconds using elapsed-realtime clock.
            val base = System.currentTimeMillis() - (totalSeconds * 1000L)
            builder
                .setUsesChronometer(true)
                .setChronometerCountDown(true)
                .setWhen(base)
                .setShowWhen(true)
        }

        val openIntent = Intent(this, MainActivity::class.java)
        val openPending = PendingIntent.getActivity(this, 0, openIntent, PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
        builder.setContentIntent(openPending)

        builder.addAction(makeAction(R.drawable.ic_pause, getString(R.string.pause), ACTION_CMD_PAUSE))
        builder.addAction(makeAction(R.drawable.ic_stop, getString(R.string.stop), ACTION_CMD_STOP))
        builder.addAction(makeAction(R.drawable.ic_done, getString(R.string.done), ACTION_CMD_DONE))

        val notification = builder.build()
        val nm = NotificationManagerCompat.from(this)
        nm.notify(NOTIFICATION_ID, notification)

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            startForeground(
                NOTIFICATION_ID,
                notification,
                android.app.ServiceInfo.FOREGROUND_SERVICE_TYPE_DATA_SYNC
            )
        } else {
            startForeground(NOTIFICATION_ID, notification)
        }
    }

    private fun makeAction(icon: Int, title: String, action: String): NotificationCompat.Action {
        val intent = Intent(this, FocusService::class.java).apply { this.action = action }
        val pending = PendingIntent.getService(this, action.hashCode(), intent, PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
        return NotificationCompat.Action.Builder(icon, title, pending).build()
    }
}

object NotificationChannels {
    fun ensureCreated(context: Context) {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.O) return
        val nm = context.getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
        nm.createNotificationChannel(
            NotificationChannel(
                FocusService.CHANNEL_FOREGROUND,
                context.getString(R.string.channel_foreground_name),
                NotificationManager.IMPORTANCE_HIGH
            ).apply {
                description = context.getString(R.string.channel_foreground_description)
            }
        )
        nm.createNotificationChannel(
            NotificationChannel(
                FocusService.CHANNEL_TIMEBOX,
                context.getString(R.string.channel_timebox_name),
                NotificationManager.IMPORTANCE_DEFAULT
            ).apply {
                description = context.getString(R.string.channel_timebox_description)
            }
        )
    }
}