package com.focuscore.widget

import android.content.Context
import androidx.glance.GlanceId
import androidx.glance.action.actionStartActivity
import androidx.glance.appwidget.GlanceAppWidget
import androidx.glance.GlanceModifier
import androidx.glance.layout.Column
import androidx.glance.text.Text
import androidx.glance.text.TextStyle
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
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
                style = TextStyle(
                    color = Color.White,
                    fontSize = 18.sp
                )
            )
            androidx.glance.layout.Spacer(modifier = GlanceModifier.fillMaxWidth().height(8.dp))
            Text(
                text = context.getString(R.string.today_widget_title),
                style = TextStyle(
                    color = Color.White,
                    fontSize = 16.sp
                )
            )
            androidx.glance.layout.Spacer(modifier = GlanceModifier.fillMaxWidth().height(8.dp))
            // Placeholder top 3 tasks for today (wire to native once bindings are generated):
            val style = TextStyle(color = Color.White, fontSize = 14.sp)
            Text(text = "Review quarterly goals", style = style)
            Text(text = "Write weekly report", style = style)
            Text(text = "Team sync", style = style)
        }
    }
}