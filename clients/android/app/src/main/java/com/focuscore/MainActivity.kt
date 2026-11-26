package com.focuscore

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent {
            FocusCoreApp()
        }
    }
}

@Composable
fun FocusCoreApp() {
    Surface(
        modifier = Modifier.fillMaxSize(),
        color = MaterialTheme.colorScheme.background
    ) {
        Text(text = "FocusCore", modifier = Modifier.align(androidx.compose.ui.Alignment.Center), fontSize = 32.sp)
        Text(text = "Native task, timeboxing & Pomodoro suite.", modifier = Modifier.align(androidx.compose.ui.Alignment.Center), fontSize = 16.sp)
    }
}
