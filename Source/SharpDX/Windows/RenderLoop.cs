﻿// Copyright (c) 2010-2013 SharpDX - Alexandre Mutel
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

#if !W8CORE
using System;
using System.Globalization;
using System.Windows.Forms;
using System.Runtime.InteropServices;

using SharpDX.Win32;

namespace SharpDX.Windows
{
    /// <summary>
    /// RenderLoop provides a rendering loop infrastructure. See remarks for usage. 
    /// </summary>
    /// <remarks>
    /// Use static <see cref="Run(System.Windows.Forms.Control,SharpDX.Windows.RenderLoop.RenderCallback)"/>  
    /// method to directly use a renderloop with a render callback or use your own loop:
    /// <code>
    /// control.Show();
    /// using (var loop = new RenderLoop(control))
    /// {
    ///     while (loop.NextFrame())
    ///     {
    ///        // Perform draw operations here.
    ///     }
    /// }
    /// </code>
    /// Note that the main control can be changed at anytime inside the loop.
    /// </remarks>
    public class RenderLoop : IDisposable, IMessageFilter
    {
        private IntPtr controlHandle;
        private Control control;
        private bool isControlAlive;
        private bool switchControl;

        /// <summary>
        /// Initializes a new instance of the <see cref="RenderLoop"/> class.
        /// </summary>
        public RenderLoop() {}

        /// <summary>
        /// Initializes a new instance of the <see cref="RenderLoop"/> class.
        /// </summary>
        public RenderLoop(Control control)
        {
            Control = control;
        }

        /// <summary>
        /// Gets or sets the control to associate with the current render loop.
        /// </summary>
        /// <value>The control.</value>
        /// <exception cref="System.InvalidOperationException">Control is already disposed</exception>
        public Control Control
        {
            get
            {
                return control;
            }
            set
            {
                if(control == value) return;

                // Remove any previous control
                if(control != null && !switchControl)
                {
                    isControlAlive = false;
                    MessageFilterHook.RemoveMessageFilter(controlHandle, this); // use cached controlHandle as control can be disposed at this time
                    control.Disposed -= ControlDisposed;
                    controlHandle = IntPtr.Zero;
                }

                if (value != null && value.IsDisposed)
                {
                    throw new InvalidOperationException("Control is already disposed");
                }

                control = value;
                switchControl = true;
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the render loop should use a custom window message loop (default false).
        /// </summary>
        /// <value><c>true</c> if the render loop should use a custom windows event handler (default false); otherwise, <c>false</c>.</value>
        /// <remarks>By default, RenderLoop is using <see cref="Application.DoEvents" /> to process windows event message. Set this parameter to true to use a custom event handler that could
        /// lead to better performance. Note that using a custom windows event message handler is not compatible with <see cref="Application.AddMessageFilter" /> or any other features
        /// that are part of <see cref="Application" />.</remarks>
        public bool UseLightweightWindowMessageLoop { get; set; }

        /// <summary>
        /// Calls this method on each frame.
        /// </summary>
        /// <returns><c>true</c> if if the control is still active, <c>false</c> otherwise.</returns>
        /// <exception cref="System.InvalidOperationException">An error occured </exception>
        public bool NextFrame()
        {
            // Setup new control
            // TODO this is not completely thread-safe. We should use a lock to handle this correctly
            if (switchControl && control != null)
            {
                controlHandle = control.Handle;
                control.Disposed += ControlDisposed;
                MessageFilterHook.AddMessageFilter(control.Handle, this);
                isControlAlive = true;
                switchControl = false;
            }

            if(isControlAlive)
            {
                if(UseLightweightWindowMessageLoop)
                {
                    var localHandle = controlHandle;
                    if(localHandle != IntPtr.Zero)
                    {
                        // Previous code not compatible with Application.AddMessageFilter but faster then DoEvents
                        Win32Native.NativeMessage msg;
                        while (Win32Native.PeekMessage(out msg, localHandle, 0, 0, 0) != 0)
                        {
                            if (Win32Native.GetMessage(out msg, localHandle, 0, 0) == -1)
                            {
                                throw new InvalidOperationException(String.Format(CultureInfo.InvariantCulture,
                                    "An error happened in rendering loop while processing windows messages. Error: {0}",
                                    Marshal.GetLastWin32Error()));
                            }

                            Win32Native.TranslateMessage(ref msg);
                            Win32Native.DispatchMessage(ref msg);
                        }
                    }
                }
                else
                {
                    // Revert back to Application.DoEvents in order to support Application.AddMessageFilter
                    // Seems that DoEvents is compatible with Mono unlike Application.Run that was not running
                    // correctly.
                    Application.DoEvents();
                }
            }

            return isControlAlive || switchControl;
        }

        bool IMessageFilter.PreFilterMessage(ref Message m)
        {
            // NCDESTROY event?
            if(m.Msg == 130)
            {
                isControlAlive = false;
            }
            return false;
        }

        private void ControlDisposed(object sender, EventArgs e)
        {
            isControlAlive = false;
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Control = null;
        }

        /// <summary>
        /// Delegate for the rendering loop.
        /// </summary>
        public delegate void RenderCallback();

        /// <summary>
        /// Runs the specified main loop in the specified context.
        /// </summary>
        public static void Run(ApplicationContext context, RenderCallback renderCallback)
        {
            Run(context.MainForm, renderCallback);
        }

        /// <summary>
        /// Runs the specified main loop for the specified windows form.
        /// </summary>
        /// <param name="form">The form.</param>
        /// <param name="renderCallback">The rendering callback.</param>
        /// <param name="useLightweightWindowMessageLoop">if set to <c>true</c> this method is using a lightweight window message loop.</param>
        /// <exception cref="System.ArgumentNullException">form
        /// or
        /// renderCallback</exception>
        public static void Run(Control form, RenderCallback renderCallback, bool useLightweightWindowMessageLoop = false)
        {
            if(form == null) throw new ArgumentNullException("form");
            if(renderCallback == null) throw new ArgumentNullException("renderCallback");

            form.Show();
            using(var renderLoop = new RenderLoop(form) { UseLightweightWindowMessageLoop =  useLightweightWindowMessageLoop})
            {
                while(renderLoop.NextFrame())
                {
                    renderCallback();
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether this instance is application idle.
        /// </summary>
        /// <value>
        /// 	<c>true</c> if this instance is application idle; otherwise, <c>false</c>.
        /// </value>
        public static bool IsIdle
        {
            get
            {
                Win32Native.NativeMessage msg;
                return (bool)(Win32Native.PeekMessage(out msg, IntPtr.Zero, 0, 0, 0) == 0);
            }
        }
   }
}
#endif