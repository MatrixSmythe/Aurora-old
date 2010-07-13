/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSimulator Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Remoting.Lifetime;
using log4net;
using Aurora.ScriptEngine.AuroraDotNetEngine;
using Aurora.ScriptEngine.AuroraDotNetEngine.APIs.Interfaces;
using Aurora.ScriptEngine.AuroraDotNetEngine.CompilerTools;

namespace Aurora.ScriptEngine.AuroraDotNetEngine.Runtime
{
    public class Executor
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        
        /// <summary>
        /// Contains the script to execute functions in.
        /// </summary>
        protected IScript m_Script;

        protected Dictionary<string, scriptEvents> m_eventFlagsMap = new Dictionary<string, scriptEvents>();
        protected Dictionary<Guid, IEnumerator> m_enumerators = new Dictionary<Guid, IEnumerator>();

        [Flags]
        public enum scriptEvents : int
        {
            None = 0,
            attach = 1,
            collision = 16,
            collision_end = 32,
            collision_start = 64,
            control = 128,
            dataserver = 256,
            email = 512,
            http_response = 1024,
            land_collision = 2048,
            land_collision_end = 4096,
            land_collision_start = 8192,
            at_target = 16384,
            at_rot_target = 16777216,
            listen = 32768,
            money = 65536,
            moving_end = 131072,
            moving_start = 262144,
            not_at_rot_target = 524288,
            not_at_target = 1048576,
            remote_data = 8388608,
            run_time_permissions = 268435456,
            state_entry = 1073741824,
            state_exit = 2,
            timer = 4,
            touch = 8,
            touch_end = 536870912,
            touch_start = 2097152,
            object_rez = 4194304
        }

        // Cache functions by keeping a reference to them in a dictionary
        private Dictionary<string, MethodInfo> Events = new Dictionary<string, MethodInfo>();
        private Dictionary<string, scriptEvents> m_stateEvents = new Dictionary<string, scriptEvents>();
        private Type m_scriptType;
        public Executor(IScript script)
        {
            m_Script = script;
            initEventFlags();
        }

        public scriptEvents GetStateEventFlags(string state)
        {
            //m_log.Debug("Get event flags for " + state);

            // Check to see if we've already computed the flags for this state
            scriptEvents eventFlags = scriptEvents.None;
            if (m_stateEvents.TryGetValue(state, out eventFlags))
            {
                return eventFlags;
            }

            if (m_scriptType == null)
                m_scriptType = m_Script.GetType();
            try
            {
                // Fill in the events for this state, cache the results in the map
                foreach (KeyValuePair<string, scriptEvents> kvp in m_eventFlagsMap)
                {
                    MethodInfo ev = null;
                    string evname = state + "_event_" + kvp.Key;
                    //m_log.Debug("Trying event "+evname);

                    if (!Events.TryGetValue(evname, out ev))
                        ev = m_scriptType.GetMethod(evname);
                    if (ev != null)
                        //m_log.Debug("Found handler for " + kvp.Key);
                        eventFlags |= kvp.Value;
                }
            }
            catch (Exception)
            {
                //m_log.Debug("Exeption in GetMethod:\n"+e.ToString());
            }

            // Save the flags we just computed and return the result
            if (eventFlags != 0)
                m_stateEvents.Add(state, eventFlags);

            //m_log.Debug("Returning {0:x}", eventFlags);
            return eventFlags;
        }

        public Guid ExecuteEvent(string state, string FunctionName, object[] args, Guid Start, out Exception ex)
        {
            ex = null;
            try
            {
                // IMPORTANT: Types and MemberInfo-derived objects require a LOT of memory.
                // Instead use RuntimeTypeHandle, RuntimeFieldHandle and RunTimeHandle (IntPtr) instead!
                string EventName = state + "_event_" + FunctionName;

                //#if DEBUG
                //m_log.Debug("ScriptEngine: Script event function name: " + EventName);
                //#endif

                #region Find Event

                MethodInfo ev = null;
                if(m_scriptType == null)
                    m_scriptType = m_Script.GetType();

                if (!Events.TryGetValue(EventName, out ev))
                {
                    // Not found, create
                    try
                    {
                        ev = m_scriptType.GetMethod(EventName);
                        Events.Add(EventName, ev);
                    }
                    catch
                    {
                        if (!Events.ContainsKey(EventName))
                            // Event name not found, cache it as not found
                            Events.Add(EventName, null);
                    }
                }
                if (ev == null) // No event by that event name!
                {
                    //Attempt to find it just by name

                    if (!Events.TryGetValue(EventName, out ev))
                    {
                        // Not found, create
                        try
                        {
                            ev = m_scriptType.GetMethod(FunctionName);
                            Events.Add(FunctionName, ev);
                        }
                        catch
                        {
                            if (!Events.ContainsKey(FunctionName))
                                // Event name not found, cache it as not found
                                Events.Add(FunctionName, null);
                        }
                    }
                    if (ev == null) // No event by that name!
                    {
                        //m_log.Debug("ScriptEngine Can not find any event named:" + EventName);
                        return new Guid();
                    }
                }
                #endregion

                return FireAsEnumerator(Start, ev, args);
            }
            catch(Exception exception)
            {
                if (ex == null)
                    ex = exception;
            }
            return Guid.Empty;
        }

        public Guid FireAsEnumerator(Guid Start, MethodInfo ev, object[] args)
        {
            IEnumerator thread = null;
            if (Start != Guid.Empty)
            {
                m_enumerators.TryGetValue(Start, out thread);
            }
            else
            {
                thread = (IEnumerator)ev.Invoke(m_Script, args);
            }
            int i = 0;
            bool running = false;
            while (i < i + 10)
            {
                i++;
                try
                {
                    running = thread.MoveNext();
                    if (!running)
                    {
                        lock (m_enumerators)
                        {
                            if (m_enumerators.ContainsKey(Start))
                                m_enumerators.Remove(Start);
                        }
                        return Guid.Empty;
                    }

                }
                catch (TargetInvocationException tie)
                {
                    // Grab the inner exception and rethrow it, unless the inner
                    // exception is an EventAbortException as this indicates event
                    // invocation termination due to a state change.
                    // DO NOT THROW JUST THE INNER EXCEPTION!
                    // FriendlyErrors depends on getting the whole exception!
                    //
                    if (!(tie.InnerException is EventAbortException))
                        throw;
                }
            }
            if (Start == Guid.Empty)
            {
                Start = System.Guid.NewGuid();
                m_enumerators.Add(Start, thread);
            }
            return Start;
        }

        protected void initEventFlags()
        {
            // Initialize the table if it hasn't already been done
            if (m_eventFlagsMap.Count > 0)
                return;

            m_eventFlagsMap.Add("attach", scriptEvents.attach);
            m_eventFlagsMap.Add("at_rot_target", scriptEvents.at_rot_target);
            m_eventFlagsMap.Add("at_target", scriptEvents.at_target);
            // m_eventFlagsMap.Add("changed",(long)scriptEvents.changed);
            m_eventFlagsMap.Add("collision", scriptEvents.collision);
            m_eventFlagsMap.Add("collision_end", scriptEvents.collision_end);
            m_eventFlagsMap.Add("collision_start", scriptEvents.collision_start);
            m_eventFlagsMap.Add("control", scriptEvents.control);
            m_eventFlagsMap.Add("dataserver", scriptEvents.dataserver);
            m_eventFlagsMap.Add("email", scriptEvents.email);
            m_eventFlagsMap.Add("http_response", scriptEvents.http_response);
            m_eventFlagsMap.Add("land_collision", scriptEvents.land_collision);
            m_eventFlagsMap.Add("land_collision_end", scriptEvents.land_collision_end);
            m_eventFlagsMap.Add("land_collision_start", scriptEvents.land_collision_start);
            // m_eventFlagsMap.Add("link_message",scriptEvents.link_message);
            m_eventFlagsMap.Add("listen", scriptEvents.listen);
            m_eventFlagsMap.Add("money", scriptEvents.money);
            m_eventFlagsMap.Add("moving_end", scriptEvents.moving_end);
            m_eventFlagsMap.Add("moving_start", scriptEvents.moving_start);
            m_eventFlagsMap.Add("not_at_rot_target", scriptEvents.not_at_rot_target);
            m_eventFlagsMap.Add("not_at_target", scriptEvents.not_at_target);
            // m_eventFlagsMap.Add("no_sensor",(long)scriptEvents.no_sensor);
            // m_eventFlagsMap.Add("on_rez",(long)scriptEvents.on_rez);
            m_eventFlagsMap.Add("remote_data", scriptEvents.remote_data);
            m_eventFlagsMap.Add("run_time_permissions", scriptEvents.run_time_permissions);
            // m_eventFlagsMap.Add("sensor",(long)scriptEvents.sensor);
            m_eventFlagsMap.Add("state_entry", scriptEvents.state_entry);
            m_eventFlagsMap.Add("state_exit", scriptEvents.state_exit);
            m_eventFlagsMap.Add("timer", scriptEvents.timer);
            m_eventFlagsMap.Add("touch", scriptEvents.touch);
            m_eventFlagsMap.Add("touch_end", scriptEvents.touch_end);
            m_eventFlagsMap.Add("touch_start", scriptEvents.touch_start);
            m_eventFlagsMap.Add("object_rez", scriptEvents.object_rez);
        }

        public void ResetStateEventFlags()
        {
            m_stateEvents.Clear();
            m_enumerators.Clear();
            m_scriptType = null;
        }
    }
}