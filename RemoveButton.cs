using ColossalFramework;
using ColossalFramework.UI;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace RemoveStuckVehicles
{
    internal class RemoveButton : MonoBehaviour
    {
        private Settings _settings;
        private Helper _helper;

        private List<UIButton> _buttons;

        private void Awake()
        {
            _settings = Settings.Instance;
            _helper = Helper.Instance;

            _buttons = new List<UIButton>();
        }

        private void Start()
        {
            foreach (UIDynamicPanels.DynamicPanelInfo dynamicPanel in UIView.library.m_DynamicPanels)
            {
                VehicleWorldInfoPanel[] panels = dynamicPanel.instance.gameObject.GetComponents<VehicleWorldInfoPanel>();

                foreach (VehicleWorldInfoPanel panel in panels)
                {
                    UIButton button = panel.component.AddUIComponent<UIButton>();

                    button.eventClick += RemoveButtonHandler;

                    button.text = Translation.GetString("Remove_Vehicle");
                    button.name = String.Format("{0} :: {1}", _settings.Tag, button.text);
                    button.autoSize = true;

                    UIButton target = panel.Find<UIButton>("Target");

                    button.font              = target.font;
                    button.textScale         = target.textScale;
                    button.textColor         = target.textColor;
                    button.disabledTextColor = target.disabledTextColor;
                    button.hoveredTextColor  = target.hoveredTextColor;
                    button.focusedTextColor  = target.focusedTextColor;
                    button.pressedTextColor  = target.pressedTextColor;

                    button.useDropShadow     = target.useDropShadow;
                    button.dropShadowColor   = target.dropShadowColor;
                    button.dropShadowOffset  = target.dropShadowOffset;

                    button.AlignTo(panel.Find<UILabel>("Type"), UIAlignAnchor.TopRight);
                    button.relativePosition += new Vector3(button.width + 7, 0);

                    _buttons.Add(button);
                }
            }
        }

        private void Update()
        {
            if ((Singleton<SimulationManager>.instance.m_currentTickIndex & 15) == 15)
            {
                if (!WorldInfoPanel.AnyWorldInfoPanelOpen()) return;

                InstanceID id = WorldInfoPanel.GetCurrentInstanceID();

                if (id.IsEmpty) return;

                if (id.Vehicle == 0 && id.ParkedVehicle == 0)
                {
                    foreach (UIButton button in _buttons)
                        button.Hide();
                }
                else
                {
                    foreach (UIButton button in _buttons)
                        button.Show();
                }
            }
        }

        private void OnDestroy()
        {
            foreach (UIButton button in _buttons)
                UnityEngine.Object.Destroy(button.gameObject);
        }

        private void RemoveButtonHandler(UIComponent component, UIMouseEventParameter param)
        {
            InstanceID id = WorldInfoPanel.GetCurrentInstanceID();

            if (id.IsEmpty) return;

            if (id.Vehicle != 0)
            {
                Helper.Instance.NotifyPlayer($"Registering vehicle {id.Vehicle} for removal.");
                _helper.ManualRemovalRequests.Add(id.Vehicle);
            }
            else if (id.ParkedVehicle != 0)
            {
                InstanceID _selected;
                InstanceID _dummy = default(InstanceID);
                if (WorldInfoPanel.AnyWorldInfoPanelOpen())
                {
                    _selected = WorldInfoPanel.GetCurrentInstanceID();

                    if (_selected.IsEmpty || _selected.ParkedVehicle == 0)
                        _selected = default(InstanceID);
                }
                else
                    _selected = default(InstanceID);

                if (!_selected.IsEmpty && _selected.ParkedVehicle == id.ParkedVehicle)
                {
                    WorldInfoPanel.HideAllWorldInfoPanels();

                    if (!InstanceManager.IsValid(_dummy) || _dummy.ParkedVehicle == id.ParkedVehicle)
                    {
                        _dummy = default(InstanceID);
                        _dummy.Type = InstanceType.ParkedVehicle;
                    }

                    Singleton<InstanceManager>.instance.SelectInstance(_dummy);
                    Singleton<InstanceManager>.instance.FollowInstance(_dummy);
                }

                Singleton<VehicleManager>.instance.m_parkedVehicles.m_buffer[(int)id.ParkedVehicle].m_flags |= 2;
                Singleton<VehicleManager>.instance.ReleaseParkedVehicle((ushort)id.ParkedVehicle);
            }
        }
    }
}
