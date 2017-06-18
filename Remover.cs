using ColossalFramework;
using ColossalFramework.Plugins;
using ICities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RemoveStuckVehicles
{
    public class Remover : ThreadingExtensionBase
    {
        private Settings _settings;
        private Helper _helper;

        private string _vehicle_confused = ColossalFramework.Globalization.Locale.Get("VEHICLE_STATUS_CONFUSED");
        private string _citizen_confused = ColossalFramework.Globalization.Locale.Get("CITIZEN_STATUS_CONFUSED");
        private InstanceID _selected;
        private InstanceID _dummy;

        private bool _initialized;
        public static bool _baselined;
        private bool _terminated;

        InstanceID instanceID = new InstanceID();
        bool remove_init;

        protected bool IsOverwatched()
		{
			foreach (var plugin in PluginManager.instance.GetPluginsInfo())
			{
				if (!plugin.isEnabled)
					continue;
				foreach (var assembly in plugin.GetAssemblies())
				{
					try
					{
						var attributes = assembly.GetCustomAttributes(typeof(System.Runtime.InteropServices.GuidAttribute), false);
						foreach (var attribute in attributes)
						{
							var guidAttribute = attribute as System.Runtime.InteropServices.GuidAttribute;
							if (guidAttribute == null)
								continue;
							if (guidAttribute.Value == "837B2D75-956A-48B4-B23E-A07D77D55847")
								return true;
						}
					}
					catch (TypeLoadException)
					{
						// This occurs for some types, not sure why, but we should be able to just ignore them.
					}
				}
			}

			return false;
		}

        public override void OnCreated(IThreading threading)
        {
            _settings = Settings.Instance;
            _helper = Helper.Instance;

            _selected = default(InstanceID);
            _dummy = default(InstanceID);

            _initialized = false;
            _baselined = false;
            _terminated = false;

            base.OnCreated(threading);
        }

        public override void OnBeforeSimulationTick()
        {
            if (_terminated) return;

            if (!_helper.GameLoaded)
            {
                _initialized = false;
                _baselined = false;
                return;
            }

            base.OnBeforeSimulationTick();
        }

        public override void OnUpdate(float realTimeDelta, float simulationTimeDelta)
        {
            if (_terminated) return;

            if (!_helper.GameLoaded) return;

            try
            {
                if (!_initialized)
                {
                    if (!IsOverwatched())
                    {
                        _helper.NotifyPlayer("Skylines Overwatch not found. Terminating...");
                        _terminated = true;

                        return;
					}
					else
						_helper.NotifyPlayer($"Skylines Overwatch found, initialising {this.GetType()}");

					SkylinesOverwatch.Settings.Instance.Enable.VehicleMonitor = true;
                    SkylinesOverwatch.Settings.Instance.Enable.HumanMonitor = true;
                    SkylinesOverwatch.Settings.Instance.Enable.BuildingMonitor = true;

                    _initialized = true;

                    _helper.NotifyPlayer("Initialized");

                    
                }
                else if (!_baselined)
                {
                    remove_init = false;
                    SkylinesOverwatch.Data data = SkylinesOverwatch.Data.Instance;
                    foreach (ushort i in data.Vehicles)
                    {
                        Vehicle v = Singleton<VehicleManager>.instance.m_vehicles.m_buffer[(int)i];

                        bool isBlocked = Identity.ModConf.RemoveBlockedVehicles && !data.IsCar(i) && v.m_blockCounter >= 64; // we will let the game decide when to remove a blocked car
                        bool isConfused = Identity.ModConf.RemoveConfusedVehicles && v.Info.m_vehicleAI.GetLocalizedStatus(i, ref v, out instanceID) == _vehicle_confused;

                        if (!isBlocked && !isConfused)
                            continue;

                        RemoveVehicle(i);
                    }
                    if (Identity.ModConf.RemoveConfusedCitizensVehicles)
                    {
                        deleteSelectedVehicle();
                        foreach (uint i in SkylinesOverwatch.Data.Instance.Humans)
                        {
                            deleteParkedVehicle(i);
                        }
                    }

                    _baselined = true;
                }
                else
                {
                    remove_init = false;
                    if ((Singleton<SimulationManager>.instance.m_currentFrameIndex / 16 % 8) == 0)
                    {
                        SkylinesOverwatch.Data data = SkylinesOverwatch.Data.Instance;

                        foreach (ushort i in data.VehiclesUpdated)
                        {
                            Vehicle v = Singleton<VehicleManager>.instance.m_vehicles.m_buffer[(int)i];

                            bool isBlocked = Identity.ModConf.RemoveBlockedVehicles && !data.IsCar(i) && v.m_blockCounter >= 64; // we will let the game decide when to remove a blocked car
                            bool isConfused = Identity.ModConf.RemoveConfusedVehicles && v.Info.m_vehicleAI.GetLocalizedStatus(i, ref v, out instanceID) == _vehicle_confused;

                            if (!isBlocked && !isConfused)
                                continue;

                            RemoveVehicle(i);
                        }
                    }

                    if (Identity.ModConf.RemoveConfusedCitizensVehicles)
                    {
                        deleteSelectedVehicle();
                        foreach (uint i in SkylinesOverwatch.Data.Instance.HumansUpdated)
                        {
                            deleteParkedVehicle(i);
                        }
                        foreach (ushort j in SkylinesOverwatch.Data.Instance.BuildingsUpdated)
                        {
                            uint num = Singleton<BuildingManager>.instance.m_buildings.m_buffer[j].m_citizenUnits;
                            while (num != 0u)
                            {
                                uint nextUnit = Singleton<CitizenManager>.instance.m_units.m_buffer[(int)((UIntPtr)num)].m_nextUnit;
                                for (int i = 0; i < 5; i++)
                                {
                                    uint citizen = Singleton<CitizenManager>.instance.m_units.m_buffer[(int)((UIntPtr)num)].GetCitizen(i);
                                    if (citizen != 0u)
                                    {
                                        deleteParkedVehicle(citizen);
                                    }
                                }
                                num = nextUnit;
                            }
                        }
                    }

                    if (_helper.ManualRemovalRequests.Any())
                        _helper.NotifyPlayer($"Removing {_helper.ManualRemovalRequests.Count} manually requested vehicle(s)");
                    foreach (ushort i in _helper.ManualRemovalRequests)
                        RemoveVehicle(i, true);

                    _helper.ManualRemovalRequests.Clear();
                }
            }
            catch (Exception e)
            {
                string error = String.Format("Failed to {0}\r\n", !_initialized ? "initialize" : "update");
                error += String.Format("Error: {0}\r\n", e.Message);
                error += "\r\n";
                error += "==== STACK TRACE ====\r\n";
                error += e.StackTrace;

                _helper.Log(error);
                _helper.NotifyPlayer($"Skylines Stuck Vehicles Remover Terminated:{Environment.NewLine}{error}");

                if (!_initialized)
                    _terminated = true;
            }

            base.OnUpdate(realTimeDelta, simulationTimeDelta);
        }

        public void deleteSelectedVehicle()
        {
            if (!remove_init)
            {
                InstanceID _selected;
                if (WorldInfoPanel.AnyWorldInfoPanelOpen())
                {
                    _selected = WorldInfoPanel.GetCurrentInstanceID();

                    if (_selected.IsEmpty || _selected.ParkedVehicle == 0)
                        _selected = default(InstanceID);
                }
                else
                    _selected = default(InstanceID);
                remove_init = true;
            }
            
            if (!_selected.IsEmpty)
            {
                uint citizen = _selected.Citizen;

                if (!SkylinesOverwatch.Data.Instance.IsResident(citizen))
                    return;

                CitizenInfo citizenInfo = Singleton<CitizenManager>.instance.m_citizens.m_buffer[(int)((UIntPtr)citizen)].GetCitizenInfo(citizen);
                InstanceID instanceID2;
                if (citizenInfo.m_citizenAI.GetLocalizedStatus(citizen, ref Singleton<CitizenManager>.instance.m_citizens.m_buffer[(int)((UIntPtr)citizen)], out instanceID2) == _citizen_confused)
                {
                    WorldInfoPanel.HideAllWorldInfoPanels();

                    if (!InstanceManager.IsValid(_dummy) || _dummy.ParkedVehicle == _selected.ParkedVehicle)
                    {
                        _dummy = default(InstanceID);
                        _dummy.Type = InstanceType.ParkedVehicle;
                    }

                    Singleton<InstanceManager>.instance.SelectInstance(_dummy);
                    Singleton<InstanceManager>.instance.FollowInstance(_dummy);

                    deleteParkedVehicle(_selected.ParkedVehicle);
                }
            }
        }

        public void deleteParkedVehicle(uint citizenId)
        {
            var citizenManager = Singleton<CitizenManager>.instance;
            if (citizenManager = null)
            {
                _helper.Log("Citizen Manager is null");
                return;
            }

            var vehicleManager = Singleton<VehicleManager>.instance;
            if (vehicleManager == null)
            {
                _helper.Log("Vehicle Manager is null");
                return;
            }

            var dataInstance = SkylinesOverwatch.Data.Instance;
            if (dataInstance == null)
            {
                _helper.Log("Vehicle Manager is null");
                return;
            }

            if (dataInstance.IsResident(citizenId))
                return;

            Citizen citizen;
            {
                var citizenN = citizenManager.m_citizens?.m_buffer?[(int)((UIntPtr)citizenId)];
                if (!citizenN.HasValue)
                {
                    _helper.Log($"`{nameof(citizenManager)}.{nameof(citizenManager.m_citizens)}` or `{nameof(citizenManager)}.{nameof(citizenManager.m_citizens)}.{nameof(citizenManager.m_citizens.m_buffer)}` is null.");
                    return;
                }
                citizen = citizenN.Value;
            }
            var citizenInfo = citizen.GetCitizenInfo(citizenId);


            InstanceID instanceID2;

            var citizenAi = citizenInfo.m_citizenAI;
            if (citizenAi == null)
                return;

            if (citizenAi.GetLocalizedStatus(citizenId, ref citizen, out instanceID2) != _citizen_confused)
                return;
            ushort parkedVehicleId = citizen.m_parkedVehicle;

            if (parkedVehicleId <= 0)
                return;

            VehicleParked parkedVehicle;
            {
                var parkedVehicleN = vehicleManager.m_parkedVehicles?.m_buffer?[parkedVehicleId];
                if (!parkedVehicleN.HasValue)
                {
                    _helper.Log($"`{nameof(vehicleManager)}.{nameof(vehicleManager.m_parkedVehicles)}` or `{nameof(vehicleManager)}.{nameof(vehicleManager.m_parkedVehicles)}.{nameof(vehicleManager.m_parkedVehicles.m_buffer)}` is null.");
                    return;
                }
                parkedVehicle = parkedVehicleN.Value;
            }
            parkedVehicle.m_flags |= 2;
            vehicleManager.ReleaseParkedVehicle(parkedVehicleId);
        }

        public override void OnReleased ()
        {
            _initialized = false;
            _baselined = false;
            _terminated = false;

            base.OnReleased();
        }

        private void RemoveVehicle(ushort vehicle, Boolean manual = false)
        {
            if (!remove_init)
            {
                if (WorldInfoPanel.AnyWorldInfoPanelOpen())
                {
                    _selected = WorldInfoPanel.GetCurrentInstanceID();

                    if (_selected.IsEmpty || _selected.Vehicle == 0)
                        _selected = default(InstanceID);
                }
                else
                    _selected = default(InstanceID);
                remove_init = true;
            }

            if (!_selected.IsEmpty && _selected.Vehicle == vehicle)
            {
                WorldInfoPanel.HideAllWorldInfoPanels();

                if (!InstanceManager.IsValid(_dummy) || _dummy.Vehicle == vehicle)
                {
                    _dummy = default(InstanceID);
                    _dummy.Type = InstanceType.Vehicle;

                    foreach (ushort i in SkylinesOverwatch.Data.Instance.Vehicles)
                    {
                        if (i == vehicle) continue;

                        _dummy.Vehicle = i;
                        break;
                    }
                }

                Singleton<InstanceManager>.instance.SelectInstance(_dummy);
                Singleton<InstanceManager>.instance.FollowInstance(_dummy);
            }

            VehicleManager instance = Singleton<VehicleManager>.instance;

            HashSet<ushort> removals = new HashSet<ushort>();
            ushort current = vehicle;

            while (current != 0)
            {
                removals.Add(current);

                current = instance.m_vehicles.m_buffer[(int)current].m_trailingVehicle;
            }

            current = instance.m_vehicles.m_buffer[(int)vehicle].m_firstCargo;

            while (current != 0)
            {
                removals.Add(current);

                current = instance.m_vehicles.m_buffer[(int)current].m_nextCargo;
            }

            foreach (ushort i in removals)
            {
                var targetVehicle = i;
                var thread = new System.Threading.Thread(() =>
                {
                    try
                    {
                        instance.ReleaseVehicle(i);
                    }
                    catch (Exception e)
                    {
                        string error = String.Format("Failed to release {0}\r\n", i);
                        error += String.Format("Error: {0}\r\n", e.Message);
                        error += "\r\n";
                        error += "==== STACK TRACE ====\r\n";
                        error += e.StackTrace;
                        _helper.Log(error);
                        if (manual)
                            _helper.NotifyPlayer($"Failed to remove vehicle: {i}{Environment.NewLine}{error}");
                    }
                });
                thread.Start();
                thread.Join();
            }

            SkylinesOverwatch.Helper.Instance.RequestVehicleRemoval(vehicle);
            if (manual)
                _helper.NotifyPlayer($"Successfully removed vehicle: {vehicle}");
        }
    }
}

