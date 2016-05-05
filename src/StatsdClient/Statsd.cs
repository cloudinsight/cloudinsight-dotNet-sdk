﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;

namespace StatsdClient
{
    public interface ICommandType { }

    public class Statsd : IStatsd
    {
        private IStopWatchFactory StopwatchFactory { get; set; }
        private IStatsdUDP Udp { get; set; }
        private IRandomGenerator RandomGenerator { get; set; }

        private readonly string _prefix;

        public List<string> Commands
        {
            get { return _commands; }
            private set { _commands = value; }
        }

        private List<string> _commands = new List<string>();

        public abstract class Metric : ICommandType
        {
            private static readonly Dictionary<Type, string> _commandToUnit = new Dictionary<Type, string>
                                                                {
                                                                    {typeof (Counting), "c"},
                                                                    {typeof (Timing), "ms"},
                                                                    {typeof (Gauge), "g"},
                                                                    {typeof (Histogram), "h"},
                                                                    {typeof (Meter), "m"},
                                                                    {typeof (Set), "s"}
                                                                };

            public static string GetCommand<TCommandType, T>(string prefix, string name, T value, double sampleRate, string[] tags) where TCommandType : Metric
            {
                string full_name = prefix + name;
                string unit = _commandToUnit[typeof(TCommandType)];
                // It would be cleaner to do this with StringBuilder, but we want sending stats to be as fast as possible
                if (sampleRate == 1.0 && (tags == null || tags.Length == 0))
                    return string.Format(CultureInfo.InvariantCulture, "{0}:{1}|{2}", full_name, value, unit);
                else if (sampleRate == 1.0 && (tags == null || tags.Length > 0))
                    return string.Format(CultureInfo.InvariantCulture, "{0}:{1}|{2}|#{3}", full_name, value, unit, string.Join(",", tags));
                else if (sampleRate != 1.0 && (tags == null || tags.Length == 0))
                    return string.Format(CultureInfo.InvariantCulture, "{0}:{1}|{2}|@{3}", full_name, value, unit, sampleRate);
                else // { if (sampleRate != 1 && (tags == null || tags.Length > 0)) }
                    return string.Format(CultureInfo.InvariantCulture, "{0}:{1}|{2}|@{3}|#{4}", full_name, value, unit, sampleRate,
                                         string.Join(",", tags));
            }
        }

        public class Counting : Metric { }
        public class Timing : Metric { }
        public class Gauge : Metric { }
        public class Histogram : Metric { }
        public class Meter : Metric { }
        public class Set : Metric { }


        public Statsd(IStatsdUDP udp, IRandomGenerator randomGenerator, IStopWatchFactory stopwatchFactory, string prefix)
        {
            StopwatchFactory = stopwatchFactory;
            Udp = udp;
            RandomGenerator = randomGenerator;
            _prefix = prefix;
        }

        public Statsd(IStatsdUDP udp, IRandomGenerator randomGenerator, IStopWatchFactory stopwatchFactory)
            : this(udp, randomGenerator, stopwatchFactory, string.Empty) { }

        public Statsd(IStatsdUDP udp, string prefix)
            : this(udp, new RandomGenerator(), new StopWatchFactory(), prefix) { }

        public Statsd(IStatsdUDP udp)
            : this(udp, "") { }

        public void Add<TCommandType, T>(string name, T value, double sampleRate = 1.0, string[] tags = null) where TCommandType : Metric
        {
            _commands.Add(Metric.GetCommand<TCommandType, T>(_prefix, name, value, sampleRate, tags));
        }

        public void Send<TCommandType, T>(string name, T value, double sampleRate = 1.0, string[] tags = null) where TCommandType : Metric
        {
            if (RandomGenerator.ShouldSend(sampleRate))
            {
                Send(Metric.GetCommand<TCommandType, T>(_prefix, name, value, sampleRate, tags));
            }
        }

        public void Send(string command)
        {
            try
            {
                Udp.Send(command);
                // clear buffer (keep existing behavior)
                if (Commands.Count > 0)
                    Commands = new List<string>();
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.Message);
            }
        }

        public void Send()
        {
            int count = Commands.Count;
            if (count < 1) return;

            Send(1 == count ? Commands[0] : string.Join("\n", Commands.ToArray()));
        }

        public void Add(Action actionToTime, string statName, double sampleRate = 1.0, string[] tags = null)
        {
            var stopwatch = StopwatchFactory.Get();

            try
            {
                stopwatch.Start();
                actionToTime();
            }
            finally
            {
                stopwatch.Stop();
                Add<Timing, int>(statName, stopwatch.ElapsedMilliseconds(), sampleRate, tags);
            }
        }

        public void Send(Action actionToTime, string statName, double sampleRate = 1.0, string[] tags = null)
        {
            var stopwatch = StopwatchFactory.Get();

            try
            {
                stopwatch.Start();
                actionToTime();
            }
            finally
            {
                stopwatch.Stop();
                Send<Timing, int>(statName, stopwatch.ElapsedMilliseconds(), sampleRate, tags);
            }
        }
    }
}
