﻿using Content.Server.GameObjects.EntitySystems;
using SS14.Server.GameObjects;
using SS14.Shared.GameObjects;
using SS14.Shared.Interfaces.GameObjects;
using SS14.Shared.IoC;
using SS14.Shared.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using YamlDotNet.RepresentationModel;

namespace Content.Server.GameObjects.Components.Power
{
    /// <summary>
    /// Component that requires power to function
    /// </summary>
    public class PowerDeviceComponent : Component, EntitySystems.IExamine
    {
        public override string Name => "PowerDevice";

        /// <summary>
        ///     The method of draw we will try to use to place our load set via component parameter, defaults to using power providers
        /// </summary>
        public virtual DrawTypes DrawType { get; protected set; } = DrawTypes.Provider;

        /// <summary>
        ///     The power draw method we are currently connected to and using
        /// </summary>
        public DrawTypes Connected { get; protected set; } = DrawTypes.None;

        public bool Powered { get; private set; } = false;


        /// <summary>
        ///     Is an external power source currently available?
        /// </summary>
        public bool ExternalPowered
        {
            get => _externalPowered;
            set
            {
                _externalPowered = value;
                UpdatePowered();
            }
        }
        private bool _externalPowered = false;

        /// <summary>
        ///     Is an internal power source currently available?
        /// </summary>
        public bool InternalPowered
        {
            get => _internalPowered;
            set
            {
                _internalPowered = value;
                UpdatePowered();
            }
        }
        private bool _internalPowered = false;

        /// <summary>
        /// Priority for powernet draw, lower will draw first, defined in powernet.cs
        /// </summary>
        public virtual Powernet.Priority Priority { get; protected set; } = Powernet.Priority.Medium;


        private float _load = 100; //arbitrary magic number to start
        /// <summary>
        ///     Power load from this entity.
        ///     In Watts.
        /// </summary>
        public float Load
        {
            get => _load;
            set => UpdateLoad(value);
        }

        /// <summary>
        /// All the power providers that we are within range of
        /// </summary>
        public List<PowerProviderComponent> AvailableProviders = new List<PowerProviderComponent>();


        private PowerProviderComponent _provider;

        /// <summary>
        /// A power provider that will handle our load, if we are linked to any
        /// </summary>
        public PowerProviderComponent Provider
        {
            get => _provider;
            set
            {
                Connected = DrawTypes.Provider;
                if (_provider != null)
                {
                    _provider.RemoveDevice(this);
                }

                _provider = value;
                if (value != null)
                {
                    _provider.AddDevice(this);
                }
                else
                {
                    Connected = DrawTypes.None;
                }

            }
        }

        public event EventHandler<PowerStateEventArgs> OnPowerStateChanged;

        public override void OnAdd()
        {
            base.OnAdd();

            if (DrawType == DrawTypes.Node || DrawType == DrawTypes.Both)
            {
                if (!Owner.TryGetComponent(out PowerNodeComponent node))
                {
                    Owner.AddComponent<PowerNodeComponent>();
                    node = Owner.GetComponent<PowerNodeComponent>();
                }
                node.OnPowernetConnect += PowernetConnect;
                node.OnPowernetDisconnect += PowernetDisconnect;
                node.OnPowernetRegenerate += PowernetRegenerate;
            }
        }

        public override void Shutdown()
        {
            if (Owner.TryGetComponent(out PowerNodeComponent node))
            {
                if (node.Parent != null && node.Parent.HasDevice(this))
                {
                    node.Parent.RemoveDevice(this);
                }

                node.OnPowernetConnect -= PowernetConnect;
                node.OnPowernetDisconnect -= PowernetDisconnect;
                node.OnPowernetRegenerate -= PowernetRegenerate;
            }

            if (Provider != null)
            {
                Provider = null;
            }

            base.Shutdown();
        }

        public override void LoadParameters(YamlMappingNode mapping)
        {
            if (mapping.TryGetNode("drawtype", out YamlNode node))
            {
                DrawType = node.AsEnum<DrawTypes>();
            }
            if (mapping.TryGetNode("load", out node))
            {
                Load = node.AsFloat();
            }
            if (mapping.TryGetNode("priority", out node))
            {
                Priority = node.AsEnum<Powernet.Priority>();
            }
        }

        string IExamine.Examine()
        {
            if (!Powered)
            {
                return "The device is not powered";
            }
            return null;
        }

        private void UpdateLoad(float value)
        {
            var oldLoad = _load;
            _load = value;
            if (Connected == DrawTypes.Node)
            {
                var node = Owner.GetComponent<PowerNodeComponent>();
                node.Parent.UpdateDevice(this, oldLoad);
            }
            else if (Connected == DrawTypes.Provider)
            {
                Provider.UpdateDevice(this, oldLoad);
            }
        }

        /// <summary>
        ///     Updates the state of whether or not this device is powered,
        ///     and fires off events if said state has changed.
        /// </summary>
        private void UpdatePowered()
        {
            var oldPowered = Powered;
            Powered = ExternalPowered || InternalPowered;
            if (oldPowered != Powered)
            {
                if (Powered)
                {
                    OnPowerStateChanged?.Invoke(this, new PowerStateEventArgs(true));
                }
                else
                {
                    OnPowerStateChanged?.Invoke(this, new PowerStateEventArgs(false));
                }
            }
        }

        /// <summary>
        /// Register a new power provider as a possible connection to this device
        /// </summary>
        /// <param name="provider"></param>
        public void AddProvider(PowerProviderComponent provider)
        {
            AvailableProviders.Add(provider);

            if (Connected != DrawTypes.Node)
            {
                ConnectToBestProvider();
            }
        }

        /// <summary>
        /// Find the nearest registered power provider and connect to it
        /// </summary>
        private void ConnectToBestProvider()
        {
            //Any values we can connect to or are we already connected to a node, cancel!
            if (!AvailableProviders.Any() || Connected == DrawTypes.Node || Deleted)
                return;

            //Get the starting value for our loop
            var position = Owner.GetComponent<TransformComponent>().WorldPosition;
            var bestprovider = AvailableProviders[0];

            //If we are already connected to a power provider we need to do a loop to find the nearest one, otherwise skip it and use first entry
            if (Connected == DrawTypes.Provider)
            {
                var bestdistance = (bestprovider.Owner.GetComponent<TransformComponent>().WorldPosition - position).LengthSquared;

                foreach (var availprovider in AvailableProviders)
                {
                    //Find distance to new provider
                    var distance = (availprovider.Owner.GetComponent<TransformComponent>().WorldPosition - position).LengthSquared;

                    //If new provider distance is shorter it becomes new best possible provider
                    if (distance < bestdistance)
                    {
                        bestdistance = distance;
                        bestprovider = availprovider;
                    }
                }
            }

            if (Provider != bestprovider)
                Provider = bestprovider;
        }

        /// <summary>
        /// Remove a power provider from being a possible connection to this device
        /// </summary>
        /// <param name="provider"></param>
        public void RemoveProvider(PowerProviderComponent provider)
        {
            if (!AvailableProviders.Contains(provider))
                return;

            AvailableProviders.Remove(provider);

            if (provider == Provider)
            {
                Provider = null;
                ExternalPowered = false;
            }

            if (Connected != DrawTypes.Node)
            {
                ConnectToBestProvider();
            }
        }

        /// <summary>
        /// Node has become anchored to a powernet
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="eventarg"></param>
        protected virtual void PowernetConnect(object sender, PowernetEventArgs eventarg)
        {
            //This sets connected = none so it must be first
            Provider = null;

            eventarg.Powernet.AddDevice(this);
            Connected = DrawTypes.Node;
        }

        /// <summary>
        /// Powernet wire was remove so we need to regenerate the powernet
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="eventarg"></param>
        protected virtual void PowernetRegenerate(object sender, PowernetEventArgs eventarg)
        {
            eventarg.Powernet.AddDevice(this);
        }

        /// <summary>
        /// Node has become unanchored from a powernet
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="eventarg"></param>
        protected virtual void PowernetDisconnect(object sender, PowernetEventArgs eventarg)
        {
            eventarg.Powernet.RemoveDevice(this);
            Connected = DrawTypes.None;

            ConnectToBestProvider();
        }

        /// <summary>
        ///     Process mechanism to keep track of internal battery and power status.
        /// </summary>
        /// <param name="frametime">Time since the last process frame.</param>
        internal virtual void ProcessInternalPower(float frametime)
        {
            if (Owner.TryGetComponent<PowerStorageComponent>(out var storage) && storage.CanDeductCharge(Load))
            {
                // We still keep InternalPowered correct if connected externally,
                // but don't use it.
                if (!ExternalPowered)
                {
                    storage.DeductCharge(Load);
                }
                InternalPowered = true;
            }
            else
            {
                InternalPowered = false;
            }
        }
    }

    public enum DrawTypes
    {
        None = 0,
        Node = 1,
        Provider = 2,
        Both = 3,
    }

    public class PowerStateEventArgs : EventArgs
    {
        public readonly bool Powered;

        public PowerStateEventArgs(bool powered)
        {
            Powered = powered;
        }
    }
}
