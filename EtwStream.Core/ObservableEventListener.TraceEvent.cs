﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;

namespace EtwStream
{
    public static partial class ObservableEventListener
    {
        const string ManifestEventName = "ManifestData";
        const TraceEventID ManifestEventID = (TraceEventID)0xFFFE;

        /// <summary>
        /// Observe Out-of-Process ETW Realtime session by provider Name or string Guid.
        /// </summary>
        /// <param name="providerNameOrGuid">e.g.'MyEventSource'</param>
        public static IObservable<TraceEvent> FromTraceEvent(params string[] providerNameOrGuid)
        {
            var guids = new List<Guid>();
            foreach (var item in providerNameOrGuid)
            {
                Guid guid;
                if (Guid.TryParse(item, out guid))
                {
                    guids.Add(guid);
                }
                else
                {
                    guids.Add(TraceEventProviders.GetEventSourceGuidFromName(item));
                }
            }

            return FromTraceEvent(guids.ToArray());
        }

        /// <summary>
        /// Observe Out-of-Process ETW Realtime session by provider Guid.
        /// </summary>
        /// <param name="providerGuid">e.g.'2e5dba47-a3d2-4d16-8ee0-6671ffdcd7b5'</param>
        public static IObservable<TraceEvent> FromTraceEvent(params Guid[] providerGuid)
        {
            IConnectableObservable<TraceEvent> source;
            var session = new TraceEventSession("ObservableEventListenerFromTraceEventSession." + Guid.NewGuid().ToString());
            var sessionName = session.SessionName;

            try
            {
                source = session.Source.Dynamic.Observe((pName, eName) => EventFilterResponse.AcceptEvent)
                    .Do(x =>
                    {
                        if (x.EventName == ManifestEventName)
                            TraceEventExtensions.ReadSchema(x);
                    })
                    .Where(x => x.EventName != ManifestEventName && x.ID != ManifestEventID)
                    .Finally(() => session.Dispose())
                    .Publish();
                foreach (var item in providerGuid)
                {
                    session.EnableProvider(item);
                }
            }
            catch
            {
                session.Dispose();
                throw;
            }

            Task.Factory.StartNew(() =>
            {
                using (session)
                {
                    session.Source.Process();
                }
            }, TaskCreationOptions.LongRunning);

            return source.RefCount();
        }

        /// <summary>
        /// Observe Out-of-Process ETW Realtime session by specified TraceEventParser.
        /// </summary>
        public static IObservable<TData> FromTraceEvent<TParser, TData>()
            where TParser : TraceEventParser
            where TData : TraceEvent
        {
            IConnectableObservable<TData> source;

            var session = new TraceEventSession("ObservableEventListenerFromTraceEventSessionWithParser." + Guid.NewGuid().ToString());
            try
            {
                var parser = (TraceEventParser)typeof(TParser).GetConstructor(new[] { typeof(TraceEventSource) }).Invoke(new[] { session.Source });
                var guid = (Guid)typeof(TParser).GetField("ProviderGuid", BindingFlags.Public | BindingFlags.Static).GetValue(null);
                source = parser.Observe<TData>().Finally(() => session.Dispose()).Publish();
                session.EnableProvider(guid);
            }
            catch
            {
                session.Dispose();
                throw;
            }

            Task.Factory.StartNew(() =>
            {
                using (session)
                {
                    session.Source.Process();
                }
            }, TaskCreationOptions.LongRunning);

            return source.RefCount();
        }

        /// <summary>
        /// Observe Out-of-Process ETW CLR TraceEvent.
        /// </summary>
        public static IObservable<TraceEvent> FromClrTraceEvent()
        {
            IConnectableObservable<TraceEvent> source;

            var session = new TraceEventSession("ObservableEventListenerFromClrTraceEventSession." + Guid.NewGuid().ToString());
            try
            {
                var guid = Microsoft.Diagnostics.Tracing.Parsers.ClrTraceEventParser.ProviderGuid;
                source = session.Source.Clr.Observe((pName, eName) => EventFilterResponse.AcceptEvent)
                    .Where(x => x.ProviderGuid == guid && x.EventName != ManifestEventName && x.ID != ManifestEventID)
                    .Finally(() => session.Dispose())
                    .Publish();
                session.EnableProvider(guid);
            }
            catch
            {
                session.Dispose();
                throw;
            }

            Task.Factory.StartNew(state =>
            {
                using (session)
                {
                    session.Source.Process();
                }
            }, TaskCreationOptions.LongRunning);

            return source.RefCount();
        }

        /// <summary>
        /// Observe Out-of-Process ETW Kernel TraceEvent.
        /// </summary>
        public static IObservable<TraceEvent> FromKernelTraceEvent(KernelTraceEventParser.Keywords flags, KernelTraceEventParser.Keywords stackCapture = KernelTraceEventParser.Keywords.None)
        {
            IConnectableObservable<TraceEvent> source;

            var session = new TraceEventSession("ObservableEventListenerFromKernelTraceEventSession." + Guid.NewGuid().ToString());
            try
            {
                var guid = KernelTraceEventParser.ProviderGuid;
                session.EnableKernelProvider(flags, stackCapture); // needs enable before observe
                source = session.Source.Kernel.Observe((pName, eName) => EventFilterResponse.AcceptEvent)
                    .Where(x => x.ProviderGuid == guid && x.EventName != ManifestEventName && x.ID != ManifestEventID)
                    .Finally(() => session.Dispose())
                    .Publish();
            }
            catch
            {
                session.Dispose();
                throw;
            }

            Task.Factory.StartNew(state =>
            {
                using (session)
                {
                    session.Source.Process();
                }
            }, TaskCreationOptions.LongRunning);

            return source.RefCount();
        }

        /// <summary>
        /// Observe Out-of-Process ETW Kernel TraceEvent.
        /// </summary>
        public static IObservable<TData> FromKernelTraceEvent<TData>(KernelTraceEventParser.Keywords flags, KernelTraceEventParser.Keywords stackCapture = KernelTraceEventParser.Keywords.None)
            where TData : TraceEvent
        {
            IConnectableObservable<TData> source;
            var session = new TraceEventSession("ObservableEventListenerFromKernelTraceEventSession." + Guid.NewGuid().ToString());
            try
            {
                session.EnableKernelProvider(flags, stackCapture);
                source = session.Source.Kernel.Observe<TData>().Finally(() => session.Dispose()).Publish();
            }
            catch
            {
                session.Dispose();
                throw;
            }

            Task.Factory.StartNew(state =>
            {
                using (session)
                {
                    session.Source.Process();
                }
            }, TaskCreationOptions.LongRunning);

            return source.RefCount();
        }

        public static void ClearAllActiveObservableEventListenerSession()
        {
            var activeSessions = TraceEventSession.GetActiveSessionNames()
                .Where(x => x.StartsWith("ObservableEventListener"))
                .Select(x => TraceEventSession.GetActiveSession(x));
            var activeCount = 0;
            foreach (var item in activeSessions)
            {
                item.Dispose();
                activeCount++;
            }
            Console.WriteLine("Cleared ActiveSession's Count:" + activeCount);
        }
    }
}