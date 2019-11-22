﻿namespace Options.MassTransit
{
    public sealed class BusOptions
    {
        public string Serializer { get; set; } = "bson";

        public string QueueNameFormat { get; set; } = "bus-{MachineName}-{AssemblyName}-{NewId}";
    }
}