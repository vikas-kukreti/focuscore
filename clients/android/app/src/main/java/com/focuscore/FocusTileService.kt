package com.focuscore

import android.content.Context
import android.content.Intent
import android.content.SharedPreferences
import android.os.Build
import android.graphics.drawable.Icon
import android.service.quicksettings.Tile
import android.service.quicksettings.TileService

/**
 * Quick settings tile: start/pause focus from the shade. Tile state mirrors the
 * shared timer_running flag written by PomodoroViewModel / FocusService.
 */
class FocusTileService : TileService() {

    private val prefs: SharedPreferences by lazy {
        getSharedPreferences("focuscore", Context.MODE_PRIVATE)
    }

    override fun onStartListening() {
        super.onStartListening()
        val tile = qsTile ?: return
        tile.state = if (prefs.getBoolean("timer_running", false)) Tile.STATE_ACTIVE else Tile.STATE_INACTIVE
        tile.label = getString(R.string.qs_tile_label)
        tile.icon = Icon.createWithResource(this, R.drawable.ic_launcher)
        tile.updateTile()
    }

    override fun onClick() {
        super.onClick()
        val tile = qsTile ?: return
        val running = prefs.getBoolean("timer_running", false)
        val intent = Intent(this, FocusService::class.java).apply {
            action = if (running) FocusService.ACTION_CMD_PAUSE else FocusService.ACTION_CMD_START
            if (!running) {
                putExtra(FocusService.EXTRA_TASK_TITLE, getString(R.string.qs_tile_start))
            }
        }
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            startForegroundService(intent)
        } else {
            startService(intent)
        }
        // Optimistically toggle; onStartListening reconciles against prefs.
        tile.state = if (running) Tile.STATE_INACTIVE else Tile.STATE_ACTIVE
        tile.updateTile()
    }
}