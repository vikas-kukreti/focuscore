package com.focuscore

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.systemBarsPadding
import androidx.compose.foundation.layout.weight
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Checkbox
import androidx.compose.material3.Divider
import androidx.compose.material3.FilledTonalButton
import androidx.compose.material3.FloatingActionButton
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ArrowBack
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.Check
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.Edit
import androidx.compose.material.icons.filled.Pause
import androidx.compose.material.icons.filled.PlayArrow
import androidx.compose.material.icons.filled.Stop
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsStateWithLifecycle
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.viewmodel.compose.viewModel
import androidx.navigation.NavType
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.navArgument
import androidx.navigation.compose.rememberNavController
import com.focuscore.model.Task
import com.focuscore.model.TimerUiState
import com.focuscore.model.TimeboxSlot
import com.focuscore.viewmodel.PomodoroViewModel
import com.focuscore.viewmodel.TimeboxViewModel
import com.focuscore.viewmodel.TodayViewModel
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        com.focuscore.core.FocusCoreClient.init()
        setContent {
            FocusCoreApp()
        }
    }

    override fun onDestroy() {
        super.onDestroy()
        com.focuscore.core.FocusCoreClient.close()
    }
}

private const val ROUTE_TODAY = "today"
private const val ROUTE_INBOX = "inbox"
private const val ROUTE_TIMEBOX = "timebox"
private const val ROUTE_TASK_DETAIL = "task_detail/{taskId}"

@Composable
fun FocusCoreApp() {
    val navController = rememberNavController()
    MaterialTheme {
        Scaffold(
            bottomBar = { FocusBottomNav(navController) },
            floatingActionButton = {
                if (navController.currentBackStackEntryAsState().value?.route == ROUTE_TODAY) {
                    QuickAddFab()
                }
            }
        ) { padding ->
            NavHost(
                navController = navController,
                startDestination = ROUTE_TODAY,
                modifier = Modifier.fillMaxSize()
            ) {
                composable(ROUTE_TODAY) {
                    TodayScreen(navController, Modifier.fillMaxSize().padding(padding))
                }
                composable(ROUTE_INBOX) {
                    InboxScreen(navController, Modifier.fillMaxSize().padding(padding))
                }
                composable(
                    route = ROUTE_TASK_DETAIL,
                    arguments = listOf(navArgument("taskId") { type = NavType.LongType })
                ) { backStackEntry ->
                    val taskId = backStackEntry.arguments?.getLong("taskId") ?: 0L
                    TaskDetailScreen(taskId, navController, Modifier.fillMaxSize().padding(padding))
                }
                composable(ROUTE_TIMEBOX) {
                    TimeboxScreen(navController, Modifier.fillMaxSize().padding(padding))
                }
            }
        }
    }
}

/** Bottom chip-style navigation: Today / Inbox / Timebox. */
@Composable
fun FocusBottomNav(navController: androidx.navigation.NavHostController) {
    val route by navController.currentBackStackEntryAsState().value?.route ?: ""
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .height(56.dp)
            .padding(horizontal = 16.dp, bottom = 8.dp)
            .systemBarsPadding()
            .background(MaterialTheme.colorScheme.surfaceContainer),
        horizontalArrangement = Arrangement.spacedBy(8.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        listOf(ROUTE_TODAY to "Today", ROUTE_INBOX to "Inbox", ROUTE_TIMEBOX to "Timebox")
            .forEach { (target, label) ->
                val selected = route == target
                if (selected) {
                    FilledTonalButton(
                        onClick = { navController.navigate(target) },
                        modifier = Modifier.weight(1f)
                    ) { Text(text = label) }
                } else {
                    TextButton(
                        onClick = { navController.navigate(target) },
                        modifier = Modifier.weight(1f)
                    ) { Text(text = label) }
                }
            }
    }
}

/** FAB that opens the quick-add task dialog on the Today screen. */
@Composable
fun QuickAddFab() {
    val viewModel: TodayViewModel = viewModel()
    var open by remember { mutableStateOf(false) }
    var title by remember { mutableStateOf("") }

    FloatingActionButton(onClick = { open = true }) {
        Icon(
            imageVector = Icons.Filled.Add,
            contentDescription = "Add task"
        )
    }

    if (open) {
        AlertDialog(
            onDismissRequest = { open = false },
            title = { Text(text = "Quick add") },
            text = {
                androidx.compose.material3.TextField(
                    value = title,
                    onValueChange = { title = it },
                    placeholder = { Text(text = "New task…") },
                    singleLine = true,
                    maxLines = 1,
                    modifier = Modifier.fillMaxWidth()
                )
            },
            confirmButton = {
                TextButton(
                    onClick = {
                        if (title.isNotBlank()) {
                            viewModel.quickAdd(title)
                            title = ""
                            open = false
                        }
                    }
                ) { Text(text = "Add") }
            },
            dismissButton = {
                TextButton(onClick = { open = false }) { Text(text = "Cancel") }
            }
        )
    }
}

private val timeFormatter = SimpleDateFormat("HH:mm", Locale.getDefault())

/** Single task row: done checkbox, title/notes, timebox chip or Schedule button. */
@Composable
fun TaskRow(task: Task, navController: androidx.navigation.NavHostController, viewModel: TodayViewModel) {
    val timeLabel = task.timeboxAt?.let { timeFormatter.format(Date(it)) }
    Row(
        modifier = Modifier.fillMaxWidth().padding(vertical = 6.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Checkbox(
            checked = task.done,
            onCheckedChange = { viewModel.toggleDone(task) }
        )
        Column(modifier = Modifier.weight(1f).padding(horizontal = 12.dp)) {
            Text(
                text = task.title,
                style = MaterialTheme.typography.titleMedium,
                color = MaterialTheme.colorScheme.onSurface.copy(alpha = if (task.done) 0.5f else 1f)
            )
            if (task.notes.isNotEmpty()) {
                Text(text = task.notes, style = MaterialTheme.typography.bodySmall, maxLines = 1)
            }
            timeLabel?.let {
                Text(
                    text = "Timebox • $it",
                    style = MaterialTheme.typography.labelSmall
                )
            }
        }
        if (task.timeboxAt == null) {
            Button(onClick = { navController.navigate(ROUTE_TIMEBOX) }) {
                Text(text = "Schedule")
            }
        }
    }
    Divider()
}

@Composable
fun TodayScreen(navController: androidx.navigation.NavHostController, modifier: Modifier) {
    val viewModel: TodayViewModel = viewModel()
    val tasks by viewModel.tasks.collectAsStateWithLifecycle()
    Column(modifier = modifier) {
        TopAppBar(title = { Text(text = "Today") })
        LazyColumn(modifier = Modifier.weight(1f)) {
            items(items = tasks, key = { it.id }) { task ->
                TaskRow(task, navController, viewModel)
            }
        }
    }
}

@Composable
fun InboxScreen(navController: androidx.navigation.NavHostController, modifier: Modifier) {
    val viewModel: TodayViewModel = viewModel()
    val tasks by viewModel.tasks.collectAsStateWithLifecycle()
    Column(modifier = modifier) {
        TopAppBar(title = { Text(text = "Inbox") })
        LazyColumn(modifier = Modifier.weight(1f)) {
            items(items = tasks, key = { it.id }) { task ->
                TaskRow(task, navController, viewModel)
            }
        }
    }
}

/** Task detail: editable notes and Pomodoro start/pause/stop. */
@Composable
fun TaskDetailScreen(taskId: Long, navController: androidx.navigation.NavHostController, modifier: Modifier) {
    val viewModel: TodayViewModel = viewModel()
    val pomodoro: PomodoroViewModel = viewModel()
    val tasks by viewModel.tasks.collectAsStateWithLifecycle()
    val uiState by pomodoro.uiState.collectAsStateWithLifecycle()
    val task = tasks.firstOrNull { it.id == taskId } ?: Task(id = taskId, title = "")
    var notes by remember(task) { mutableStateOf(task.notes) }
    var totalSeconds by remember { mutableIntStateOf(task.pomodoroTotalSeconds) }

    Column(modifier = modifier) {
        TopAppBar(
            title = { Text(text = task.title) },
            navigationIcon = {
                IconButton(onClick = { navController.popBackStack() }) {
                    Icon(imageVector = Icons.Filled.ArrowBack, contentDescription = "Back")
                }
            }
        )
        Text(text = "Notes", style = MaterialTheme.typography.titleMedium, modifier = Modifier.padding(16.dp))
        androidx.compose.material3.OutlinedTextField(
            value = notes,
            onValueChange = { notes = it },
            label = { Text(text = "Notes") },
            modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp)
        )
        Spacer(Modifier.height(16.dp))
        Text(text = "Pomodoro", style = MaterialTheme.typography.titleMedium, modifier = Modifier.padding(16.dp))
        Text(
            text = "%02d:%02d".format(uiState.remainingSeconds / 60, uiState.remainingSeconds % 60),
            style = MaterialTheme.typography.headlineMedium,
            modifier = Modifier.padding(16.dp)
        )
        Spacer(Modifier.height(8.dp))
        Row(
            modifier = Modifier.padding(horizontal = 16.dp),
            horizontalArrangement = Arrangement.spacedBy(8.dp)
        ) {
            Button(
                enabled = !uiState.isRunning,
                onClick = { pomodoro.start(task, totalSeconds) }
            ) { Text(text = "Start") }
            Button(
                enabled = uiState.isRunning,
                onClick = { pomodoro.pause() }
            ) { Text(text = "Pause") }
            Button(
                enabled = uiState.isRunning,
                onClick = { pomodoro.stop() }
            ) { Text(text = "Stop") }
        }
        Spacer(Modifier.height(12.dp))
        Row(
            modifier = Modifier.padding(horizontal = 16.dp),
            horizontalArrangement = Arrangement.spacedBy(8.dp)
        ) {
            listOf(25 * 60, 5 * 60, 15 * 60).forEach { seconds ->
                Button(
                    onClick = { totalSeconds = seconds }
                ) { Text(text = "${seconds / 60}m") }
            }
        }
    }
}

/** Timebox schedule: half-hour slots 06:00–23:00, each assignable to a task. */
@Composable
fun TimeboxScreen(navController: androidx.navigation.NavHostController, modifier: Modifier) {
    val viewModel: TimeboxViewModel = viewModel()
    val slots by viewModel.slots.collectAsStateWithLifecycle()
    var assignSlot by remember { mutableStateOf<TimeboxSlot?>(null) }
    var slotTitle by remember { mutableStateOf("") }

    Column(modifier = modifier) {
        TopAppBar(title = { Text(text = "Timebox") })
        Text(
            text = "Schedule focus blocks 06:00–23:00",
            style = MaterialTheme.typography.labelLarge,
            modifier = Modifier.padding(horizontal = 16.dp)
        )
        LazyColumn(modifier = Modifier.weight(1f)) {
            items(items = slots, key = { it.startMillis }) { slot ->
                val timeLabel = timeFormatter.format(Date(slot.startMillis))
                Row(
                    modifier = Modifier.fillMaxWidth().padding(vertical = 4.dp, horizontal = 8.dp),
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Text(text = timeLabel, style = MaterialTheme.typography.titleMedium, modifier = Modifier.width(72.dp))
                    if (slot.taskId != 0L) {
                        Text(text = slot.taskTitle, modifier = Modifier.weight(1f))
                    } else {
                        Text(text = "Empty slot", style = MaterialTheme.typography.bodyMedium, modifier = Modifier.weight(1f))
                    }
                    Button(onClick = { slotTitle = slot.taskTitle; assignSlot = slot }) {
                        Text(text = if (slot.taskId != 0L) "Change" else "Assign")
                    }
                }
                Divider()
            }
        }

        if (assignSlot != null) {
            AlertDialog(
                onDismissRequest = { assignSlot = null },
                title = { Text(text = "Schedule task") },
                text = {
                    androidx.compose.material3.TextField(
                        value = slotTitle,
                        onValueChange = { slotTitle = it },
                        placeholder = { Text(text = "Task name") },
                        singleLine = true,
                        modifier = Modifier.fillMaxWidth()
                    )
                },
                confirmButton = {
                    TextButton(onClick = {
                        if (slotTitle.isNotBlank() && assignSlot != null) {
                            viewModel.assign(slotTitle, assignSlot!!)
                            slotTitle = ""
                            assignSlot = null
                        }
                    }) { Text(text = "Schedule") }
                },
                dismissButton = {
                    TextButton(onClick = { assignSlot = null }) { Text(text = "Cancel") }
                }
            )
        }
    }
}