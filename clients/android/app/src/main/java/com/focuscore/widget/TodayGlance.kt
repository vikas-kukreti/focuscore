package com.focuscore.widget

import android.content.Context
import androidx.glance.GlanceId
import androidx.glance.action.actionStartActivity
import androidx.glance.appwidget.GlanceAppWidget
import androidx.glance.GlanceModifier
import androidx.glance.layout.Column
import androidx.glance.layout.Row
import androidx.glance.text.Text
import androidx.glance.unit.dp
import androidx.glance.text.FontSize
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.graphics.Color
import com.focuscore.MainActivity
import com.focuscore.R

/**
 * Glance home screen widget showing today's top 3 tasks.
 *
 * TODO: read the top 3 tasks for today from libfocuscore (generated UniFFI
 * bindings) or DataStore. Placeholder tasks are shown until the native client
 * is wired.
 */
class TodayGlance : GlanceAppWidget() {
    override suspend fun provideGlance(context: Context, id: GlanceId) {
        Column(modifier = GlanceModifier.fillMaxSize().padding(16.dp)) {
            Text(
                text = context.getString(R.string.app_name),
                fontSize = FontSize(18f),
                color = Color.White
            )
            androidx.glance.layout.Spacer(modifier = GlanceModifier.height(8.dp))
            Text(
                text = context.getString(R.string.today_widget_title),
                fontSize = FontSize(16f),
                color = Color.White
            )
            androidx.glance.layout.Spacer(modifier = GlanceModifier.height(8.dp))
            // Placeholder top 3 tasks for today (wire to native once bindings are generated):
            Text(text = "Review quarterly goals", fontSize = FontSize(14f), color = Color.White)
            Text(text = "Write weekly report", fontSize = FontSize(14f), color = Color.White)
            Text(text = "Team sync", fontSize = FontSize(14f), color = Color.White)
        }
    }
}