package com.focuscore.core

/**
 * Native bindings to libfocuscore (Rust core exposed via UniFFI).
 *
 * TODO: this file is a local placeholder. CI runs `uniffi-bindgen kotlin` from
 * `core/interface.udl` to generate the real Kotlin bindings in package
 * `com.focuscore.core`, and builds `libfocuscore.so` via `cargo-ndk` into
 * `app/src/main/jniLibs/<abi>/`. Replace this stub with the generated bindings
 * before shipping.
 */
object FocusCore {
    init {
        try {
            System.loadLibrary("focuscore")
        } catch (_: UnsatisfiedLinkError) {
            // libfocuscore.so is bundled by CI (cargo-ndk); absent in local builds.
        }
    }

    @JvmStatic
    external fun focuscoreOpen(): Long

    @JvmStatic
    external fun focuscoreClose(handle: Long)

    @JvmStatic
    external fun focuscoreQuickAdd(handle: Long, title: String): Long

    // ---- Timer ----
    @JvmStatic
    external fun focuscoreTimerStart(handle: Long, taskId: Long, totalSeconds: Int)

    @JvmStatic
    external fun focuscoreTimerPause(handle: Long)

    @JvmStatic
    external fun focuscoreTimerStop(handle: Long)

    @JvmStatic
    external fun focuscoreTimerRemaining(handle: Long): Long

    @JvmStatic
    external fun focuscoreTimerIsRunning(handle: Long): Boolean
}

/**
 * Thin client over the native library. Fails silently when libfocuscore.so is
 * not bundled so the UI compiles and runs against in-memory/shared-prefs state.
 */
object FocusCoreClient {
    private var handle: Long = 0
    private var opened = false

    fun init() {
        if (opened) return
        try {
            handle = FocusCore.focuscoreOpen()
            opened = true
        } catch (_: UnsatisfiedLinkError) {
            // libfocuscore.so not available yet — provided by CI (cargo-ndk).
        }
    }

    fun close() {
        if (opened) {
            try { FocusCore.focuscoreClose(handle) } catch (_: UnsatisfiedLinkError) {}
            opened = false
        }
    }

    fun quickAdd(title: String): Long {
        return try { FocusCore.focuscoreQuickAdd(handle, title) } catch (_: UnsatisfiedLinkError) { 0L }
    }

    fun timerStart(taskId: Long, totalSeconds: Int) {
        try { FocusCore.focuscoreTimerStart(handle, taskId, totalSeconds) } catch (_: UnsatisfiedLinkError) {}
    }

    fun timerPause() {
        try { FocusCore.focuscoreTimerPause(handle) } catch (_: UnsatisfiedLinkError) {}
    }

    fun timerStop() {
        try { FocusCore.focuscoreTimerStop(handle) } catch (_: UnsatisfiedLinkError) {}
    }

    fun timerRemaining(): Long {
        return try { FocusCore.focuscoreTimerRemaining(handle) } catch (_: UnsatisfiedLinkError) { 0L }
    }

    fun timerIsRunning(): Boolean {
        return try { FocusCore.focuscoreTimerIsRunning(handle) } catch (_: UnsatisfiedLinkError) { false }
    }
}
