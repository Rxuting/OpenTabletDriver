﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Platform.Pointer;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriver.Plugin.Output
{
    [PluginIgnore]
    public abstract class RelativeOutputMode : BindingHandler, IOutputMode
    {
        private List<IFilter> filters, preFilters = new List<IFilter>(), postFilters = new List<IFilter>();
        private Vector2? lastPos;
        private DateTime lastReceived;
        private Matrix3x2 transformationMatrix;

        public IEnumerable<IFilter> Filters
        {
            set
            {
                this.filters = value.ToList();
                this.preFilters.Clear();
                this.postFilters.Clear();

                foreach (var filter in this.filters)
                {
                    switch (filter.FilterStage)
                    {
                        case FilterStage.PreTranspose:
                            this.preFilters.Add(filter);
                            break;
                        case FilterStage.PostTranspose:
                            this.postFilters.Add(filter);
                            break;
                        default:
                            throw new NotSupportedException();
                    }
                }
            }
            get => this.filters;
        }

        public abstract IVirtualMouse VirtualMouse { get; }
        public IVirtualPointer Pointer => VirtualMouse;

        private Vector2 sensitivity;
        public Vector2 Sensitivity
        {
            set
            {
                this.sensitivity = value;
                UpdateTransformMatrix();
            }
            get => this.sensitivity;
        }

        private float rotation;
        public float Rotation
        {
            set
            {
                this.rotation = value;
                UpdateTransformMatrix();
            }
            get => this.rotation;
        }

        private void UpdateTransformMatrix()
        {
            this.lastReceived = default;  // Prevents cursor from jumping on sensitivity change

            this.transformationMatrix = Matrix3x2.CreateRotation(
                (float)(-Rotation * System.Math.PI / 180));

            this.transformationMatrix *= Matrix3x2.CreateScale(
                sensitivity.X * ((Digitizer?.Width / Digitizer?.MaxX) ?? 0.01f),
                sensitivity.Y * ((Digitizer?.Height / Digitizer?.MaxY) ?? 0.01f));
        }

        public TimeSpan ResetTime { set; get; }

        public virtual void Read(IDeviceReport report)
        {
            if (report is ITabletReport tabletReport)
            {
                if (Digitizer.ActiveReportID.IsInRange(tabletReport.ReportID))
                {
                    if (Transpose(tabletReport) is Vector2 pos)
                    {
                        if (VirtualMouse is IPressureHandler pressureHandler)
                            pressureHandler.SetPressure((float)tabletReport.Pressure / (float)Digitizer.MaxPressure);

                        VirtualMouse.Move(pos.X, pos.Y);
                    }
                }
            }
        }

        protected Vector2? Transpose(ITabletReport report)
        {
            var difference = DateTime.Now - this.lastReceived;
            this.lastReceived = DateTime.Now;

            var pos = report.Position;

            // Pre Filter
            foreach (IFilter filter in this.preFilters)
                pos = filter.Filter(pos);

            pos = Vector2.Transform(pos, this.transformationMatrix);

            // Post Filter
            foreach (IFilter filter in this.postFilters)
                pos = filter.Filter(pos);

            var delta = pos - this.lastPos;
            this.lastPos = pos;

            return (difference > ResetTime) ? null : delta;
        }
    }
}