using KerbalSimpit.Utilities;
using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace KerbalSimpit.KerbalSimpit.External
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    class KeyboardEmulator : MonoBehaviour
    {
        // X11 interop for Linux keyboard simulation
        [DllImport("libX11.so.6")]
        private static extern IntPtr XOpenDisplay(IntPtr display);

        [DllImport("libX11.so.6")]
        private static extern int XCloseDisplay(IntPtr display);

        [DllImport("libXtst.so.6")]
        private static extern int XTestFakeKeyEvent(IntPtr display, uint keycode, bool is_press, ulong delay);

        [DllImport("libX11.so.6")]
        private static extern int XFlush(IntPtr display);

        [DllImport("libX11.so.6")]
        private static extern uint XKeysymToKeycode(IntPtr display, ulong keysym);

        // X11 KeySym values - Function keys
        private const ulong XK_F1 = 0xffbe;
        private const ulong XK_F2 = 0xffbf;
        private const ulong XK_F3 = 0xffc0;
        private const ulong XK_F4 = 0xffc1;
        private const ulong XK_F5 = 0xffc2;
        private const ulong XK_F6 = 0xffc3;
        private const ulong XK_F7 = 0xffc4;
        private const ulong XK_F8 = 0xffc5;
        private const ulong XK_F9 = 0xffc6;
        private const ulong XK_F10 = 0xffc7;
        private const ulong XK_F11 = 0xffc8;
        private const ulong XK_F12 = 0xffc9;

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        [Serializable]
        public struct KeyboardEmulatorStruct
        {
            public byte modifier;
            public Int16 key;
        }

        private EventData<byte, object> keyboardEmulatorEvent;
        private bool uiHidden = false;
        private bool useX11 = false;
        private IntPtr display = IntPtr.Zero;

        public void Start()
        {
            keyboardEmulatorEvent = GameEvents.FindEvent<EventData<byte, object>>("onSerialReceived" + InboundPackets.KeyboardEmulator);
            if (keyboardEmulatorEvent != null) keyboardEmulatorEvent.Add(KeyboardEmulatorCallback);

            // Try to initialize X11
            try
            {
                display = XOpenDisplay(IntPtr.Zero);
                if (display != IntPtr.Zero)
                {
                    useX11 = true;
                    Debug.Log("Simpit KeyboardEmulator: Using X11 keyboard simulation (Linux)");
                }
                else
                {
                    Debug.Log("Simpit KeyboardEmulator: Using KSP API mode (X11 not available)");
                }
            }
            catch (DllNotFoundException)
            {
                Debug.Log("Simpit KeyboardEmulator: Using KSP API mode (X11 libraries not found)");
            }
        }

        public void OnDestroy()
        {
            if (keyboardEmulatorEvent != null) keyboardEmulatorEvent.Remove(KeyboardEmulatorCallback);
            
            if (display != IntPtr.Zero)
            {
                XCloseDisplay(display);
                display = IntPtr.Zero;
            }
        }

        // Convert Windows Virtual Key Code to X11 KeySym
        private ulong VirtualKeyToX11KeySym(int vkCode)
        {
            // Function keys (F1-F12): 0x70-0x7B
            if (vkCode >= 0x70 && vkCode <= 0x7B)
            {
                return 0xffbe + (ulong)(vkCode - 0x70); // XK_F1 through XK_F12
            }
            
            // Letters A-Z: 0x41-0x5A (lowercase in X11)
            if (vkCode >= 0x41 && vkCode <= 0x5A)
            {
                return (ulong)(vkCode + 32); // Convert to lowercase for X11
            }
            
            // Numbers 0-9: 0x30-0x39
            if (vkCode >= 0x30 && vkCode <= 0x39)
            {
                return (ulong)vkCode;
            }
            
            // Special keys mapping
            switch (vkCode)
            {
                case 0x0D: return 0xff0d; // VK_RETURN -> XK_Return
                case 0x1B: return 0xff1b; // VK_ESCAPE -> XK_Escape
                case 0x20: return 0x0020; // VK_SPACE -> XK_space
                case 0x09: return 0xff09; // VK_TAB -> XK_Tab
                case 0x08: return 0xff08; // VK_BACK -> XK_BackSpace
                case 0x2E: return 0xffff; // VK_DELETE -> XK_Delete
                case 0x2D: return 0xff63; // VK_INSERT -> XK_Insert
                case 0x24: return 0xff50; // VK_HOME -> XK_Home
                case 0x23: return 0xff57; // VK_END -> XK_End
                case 0x21: return 0xff55; // VK_PRIOR (Page Up) -> XK_Prior
                case 0x22: return 0xff56; // VK_NEXT (Page Down) -> XK_Next
                case 0x25: return 0xff51; // VK_LEFT -> XK_Left
                case 0x26: return 0xff52; // VK_UP -> XK_Up
                case 0x27: return 0xff53; // VK_RIGHT -> XK_Right
                case 0x28: return 0xff54; // VK_DOWN -> XK_Down
                
                // Punctuation and symbols
                case 0xBA: return 0x003b; // VK_OEM_1 -> ; :
                case 0xBB: return 0x003d; // VK_OEM_PLUS -> = +
                case 0xBC: return 0x002c; // VK_OEM_COMMA -> , <
                case 0xBD: return 0x002d; // VK_OEM_MINUS -> - _
                case 0xBE: return 0x002e; // VK_OEM_PERIOD -> . >
                case 0xBF: return 0x002f; // VK_OEM_2 -> / ?
                case 0xC0: return 0x0060; // VK_OEM_3 -> ` ~
                case 0xDB: return 0x005b; // VK_OEM_4 -> [ {
                case 0xDC: return 0x005c; // VK_OEM_5 -> \ |
                case 0xDD: return 0x005d; // VK_OEM_6 -> ] }
                case 0xDE: return 0x0027; // VK_OEM_7 -> ' "
                
                // Numpad
                case 0x60: return 0xffb0; // VK_NUMPAD0 -> XK_KP_0
                case 0x61: return 0xffb1; // VK_NUMPAD1 -> XK_KP_1
                case 0x62: return 0xffb2; // VK_NUMPAD2 -> XK_KP_2
                case 0x63: return 0xffb3; // VK_NUMPAD3 -> XK_KP_3
                case 0x64: return 0xffb4; // VK_NUMPAD4 -> XK_KP_4
                case 0x65: return 0xffb5; // VK_NUMPAD5 -> XK_KP_5
                case 0x66: return 0xffb6; // VK_NUMPAD6 -> XK_KP_6
                case 0x67: return 0xffb7; // VK_NUMPAD7 -> XK_KP_7
                case 0x68: return 0xffb8; // VK_NUMPAD8 -> XK_KP_8
                case 0x69: return 0xffb9; // VK_NUMPAD9 -> XK_KP_9
                case 0x6A: return 0xffaa; // VK_MULTIPLY -> XK_KP_Multiply
                case 0x6B: return 0xffab; // VK_ADD -> XK_KP_Add
                case 0x6D: return 0xffad; // VK_SUBTRACT -> XK_KP_Subtract
                case 0x6E: return 0xffae; // VK_DECIMAL -> XK_KP_Decimal
                case 0x6F: return 0xffaf; // VK_DIVIDE -> XK_KP_Divide
                case 0x90: return 0xff7f; // VK_NUMLOCK -> XK_Num_Lock
                
                // Modifier keys
                case 0x10: return 0xffe1; // VK_SHIFT -> XK_Shift_L
                case 0x11: return 0xffe3; // VK_CONTROL -> XK_Control_L
                case 0x12: return 0xffe9; // VK_MENU (Alt) -> XK_Alt_L
                case 0x14: return 0xffe5; // VK_CAPITAL -> XK_Caps_Lock
                case 0x91: return 0xff14; // VK_SCROLL -> XK_Scroll_Lock
                
                default:
                    Debug.LogWarning(string.Format("Simpit KeyboardEmulator: No X11 mapping for VK code 0x{0:X}", vkCode));
                    return 0;
            }
        }

        private void SendX11Key(int vkCode)
        {
            if (display == IntPtr.Zero) return;

            ulong keysym = VirtualKeyToX11KeySym(vkCode);
            if (keysym == 0) return;

            try
            {
                uint keycode = XKeysymToKeycode(display, keysym);
                if (keycode != 0)
                {
                    XTestFakeKeyEvent(display, keycode, true, 0);  // Key down
                    XTestFakeKeyEvent(display, keycode, false, 0); // Key up
                    XFlush(display);
                    Debug.Log(string.Format("Simpit KeyboardEmulator: Sent X11 key VK=0x{0:X} KeySym=0x{1:X}", vkCode, keysym));
                }
                else
                {
                    Debug.LogWarning(string.Format("Simpit KeyboardEmulator: X11 keycode is 0 for KeySym 0x{0:X}", keysym));
                }
            }
            catch (Exception e)
            {
                Debug.LogError("Simpit KeyboardEmulator: X11 key send failed - " + e.Message);
            }
        }

        public void KeyboardEmulatorCallback(byte ID, object Data)
        {
            try
            {
                KeyboardEmulatorStruct payload = KerbalSimpitUtils.ByteArrayToStructure<KeyboardEmulatorStruct>((byte[])Data);

                Debug.Log(string.Format("Simpit KeyboardEmulator: Received key=0x{0:X}, modifier={1}", payload.key, payload.modifier));

                if (useX11)
                {
                    // Use X11 to send real keypresses - supports any key dynamically
                    SendX11Key(payload.key);
                }
                else
                {
                    // Fallback to KSP API (only F1 and F2 work safely)
                    switch (payload.key)
                    {
                        case 0x70: // F1 - Screenshot
                            Debug.Log("Simpit KeyboardEmulator: F1 - Taking screenshot");
                            try
                            {
                                ScreenCapture.CaptureScreenshot("screenshot_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
                                ScreenMessages.PostScreenMessage("Screenshot saved", 2f, ScreenMessageStyle.UPPER_CENTER);
                            }
                            catch (System.Exception e)
                            {
                                Debug.LogError("Simpit KeyboardEmulator: Screenshot failed - " + e.Message);
                            }
                            break;
                            
                        case 0x71: // F2 - Toggle UI
                            Debug.Log("Simpit KeyboardEmulator: F2 - Toggling UI");
                            try
                            {
                                uiHidden = !uiHidden;
                                if (uiHidden)
                                {
                                    GameEvents.onHideUI.Fire();
                                }
                                else
                                {
                                    GameEvents.onShowUI.Fire();
                                }
                            }
                            catch (System.Exception e)
                            {
                                Debug.LogError("Simpit KeyboardEmulator: UI toggle failed - " + e.Message);
                            }
                            break;
                            
                        case 0x4D: // M - Toggle Map View
                            Debug.Log("Simpit KeyboardEmulator: M - Map view toggle (not implemented without X11)");
                            break;
                            
                        case 0x56: // V - Cycle Camera Mode
                            Debug.Log("Simpit KeyboardEmulator: V - Camera cycle (not implemented without X11)");
                            break;
                            
                        case 0x43: // C - Toggle IVA/External Camera
                            Debug.Log("Simpit KeyboardEmulator: C - Camera toggle (not implemented without X11)");
                            break;
                            
                        default:
                            Debug.LogWarning("Simpit KeyboardEmulator: Unrecognized key code 0x" + payload.key.ToString("X"));
                            break;
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Simpit KeyboardEmulator: Error processing keyboard command: " + exception.Message);
                if (KSPit.Config.Verbose)
                {
                    Debug.LogWarning(exception.ToString());
                }
            }
        }

    }




}
