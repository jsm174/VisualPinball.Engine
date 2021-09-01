﻿// Visual Pinball Engine
// Copyright (C) 2021 freezy and VPE Team
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.

// ReSharper disable InconsistentNaming

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using VisualPinball.Engine.Common;
using VisualPinball.Engine.Game.Engines;
using VisualPinball.Engine.VPT;

namespace VisualPinball.Unity
{
	[Serializable]
	public class MappingConfig
	{
		[SerializeField] public List<SwitchMapping> Switches = new List<SwitchMapping>();
		[SerializeField] public List<CoilMapping> Coils = new List<CoilMapping>();
		[SerializeField] public List<WireMapping> Wires = new List<WireMapping>();
		[SerializeField] public List<LampMapping> Lamps = new List<LampMapping>();

		public bool IsEmpty()
		{
			return (Coils == null || Coils.Count == 0)
		       && (Switches == null || Switches.Count == 0);
		}

		private static void Retrieve<T>(IEnumerable node, List<T> components, Action<Transform, List<T>> action)
		{
			foreach (Transform childTransform in node) {
				action(childTransform, components);
				Retrieve(childTransform, components, action);
			}
		}

		#region Switches

		public void PopulateSwitches(GamelogicEngineSwitch[] engineSwitches, TableAuthoring tableComponent)
		{
			var switchDevices = tableComponent.GetComponentsInChildren<ISwitchDeviceAuthoring>();

			foreach (var engineSwitch in GetSwitchIds(engineSwitches)) {
				var switchMapping = Switches.FirstOrDefault(mappingsSwitchData => mappingsSwitchData.Id == engineSwitch.Id);
				if (switchMapping == null) {

					var description = engineSwitch.Description ?? string.Empty;
					var source = GuessSwitchSource(engineSwitch);
					var device = source == ESwitchSource.Playfield ? GuessDevice(switchDevices, engineSwitch) : null;
					var deviceItem = source == ESwitchSource.Playfield && device != null ? GuessDeviceItem(engineSwitch, device) : null;
					var inputActionMap = source == SwitchSource.InputSystem
						? string.IsNullOrEmpty(engineSwitch.InputMapHint) ? InputConstants.MapCabinetSwitches : engineSwitch.InputMapHint
						: string.Empty;
					var inputAction = source == SwitchSource.InputSystem
						? string.IsNullOrEmpty(engineSwitch.InputActionHint) ? string.Empty : engineSwitch.InputActionHint
						: string.Empty;

					AddSwitch(new SwitchMapping {
						Id = engineSwitch.Id,
						InternalId = engineSwitch.InternalId,
						IsNormallyClosed = engineSwitch.NormallyClosed,
						Description = description,
						Source = source,
						InputActionMap = inputActionMap,
						InputAction = inputAction,
						Device = device,
						DeviceItem = deviceItem != null ? deviceItem.Id : string.Empty,
						Constant = engineSwitch.ConstantHint == SwitchConstantHint.AlwaysOpen ? Engine.VPT.SwitchConstant.Open : Engine.VPT.SwitchConstant.Closed
					});
				}
			}
		}


		/// <summary>
		/// Returns a sorted list of switch names from the gamelogic engine,
		/// appended with the additional names in the switch mapping. In short,
		/// the list of switch names to choose from.
		/// </summary>
		/// <param name="engineSwitches">Switch names provided by the gamelogic engine</param>
		/// <returns>All switch names</returns>
		public IEnumerable<GamelogicEngineSwitch> GetSwitchIds(GamelogicEngineSwitch[] engineSwitches)
		{
			var ids = new List<GamelogicEngineSwitch>();
			if (engineSwitches != null) {
				ids.AddRange(engineSwitches);
			}

			foreach (var mappingsSwitchData in Switches) {
				if (!ids.Exists(entry => entry.Id == mappingsSwitchData.Id))
				{
					ids.Add(new GamelogicEngineSwitch(mappingsSwitchData.Id));
				}
			}

			ids.Sort((s1, s2) => string.Compare(s1.Id, s2.Id, StringComparison.Ordinal));
			return ids;
		}

		private static ESwitchSource GuessSwitchSource(GamelogicEngineSwitch engineSwitch)
		{
			if (!string.IsNullOrEmpty(engineSwitch.DeviceHint)) {
				return ESwitchSource.Playfield;
			}

			if (engineSwitch.ConstantHint != SwitchConstantHint.None) {
				return ESwitchSource.Constant;
			}

			return !string.IsNullOrEmpty(engineSwitch.InputActionHint) ? ESwitchSource.InputSystem : ESwitchSource.Playfield;
		}

		private static ISwitchDeviceAuthoring GuessDevice(ISwitchDeviceAuthoring[] switchDevices, GamelogicEngineSwitch engineSwitch)
		{
			// match by regex if hint provided
			if (!string.IsNullOrEmpty(engineSwitch.DeviceHint)) {
				foreach (var device in switchDevices) {
					var regex = new Regex(engineSwitch.DeviceHint, RegexOptions.IgnoreCase);
					if (regex.Match(device.name).Success) {
						return device;
					}
				}
			}
			return null;
		}

		private static GamelogicEngineSwitch GuessDeviceItem(GamelogicEngineSwitch engineSwitch, ISwitchDeviceAuthoring device)
		{
			if (device.AvailableSwitches.Count() == 1) {
				return device.AvailableSwitches.First();
			}
			if (!string.IsNullOrEmpty(engineSwitch.DeviceItemHint)) {
				foreach (var deviceSwitch in device.AvailableSwitches) {
					var regex = new Regex(engineSwitch.DeviceItemHint, RegexOptions.IgnoreCase);
					if (regex.Match(deviceSwitch.Id).Success) {
						return deviceSwitch;
					}
				}
			}
			return null;
		}

		public void AddSwitch(SwitchMapping switchMapping)
		{
			Switches?.Add(switchMapping);
		}

		public void RemoveSwitch(SwitchMapping switchMapping)
		{
			Switches?.Remove(switchMapping);
		}

		public void RemoveAllSwitches()
		{
			if (Switches == null) {
				Switches = new List<SwitchMapping>();

			} else {
				Switches.Clear();
			}
		}

		#endregion

		#region Coils

		/// <summary>
		/// Auto-matches the coils provided by the gamelogic engine with the
		/// coils on the playfield.
		/// </summary>
		/// <param name="engineCoils">List of coils provided by the gamelogic engine</param>
		/// <param name="tableComponent">Table component</param>
		public void PopulateCoils(GamelogicEngineCoil[] engineCoils, TableAuthoring tableComponent)
		{
			var coilDevices = tableComponent.GetComponentsInChildren<ICoilDeviceAuthoring>();
			var holdCoils = new List<GamelogicEngineCoil>();
			foreach (var engineCoil in GetCoils(engineCoils)) {

				var coilMapping = Coils.FirstOrDefault(mappingsCoilData => mappingsCoilData.Id == engineCoil.Id);
				if (coilMapping == null) {

					if (engineCoil.IsUnused) {
						continue;
					}

					// we'll handle those in a second loop when all the main coils are added
					if (!string.IsNullOrEmpty(engineCoil.MainCoilIdOfHoldCoil)) {
						holdCoils.Add(engineCoil);
						continue;
					}

					var destination = GuessCoilDestination(engineCoil);
					var description = string.IsNullOrEmpty(engineCoil.Description) ? string.Empty : engineCoil.Description;
					var device = destination == CoilDestination.Playfield ? GuessDevice(coilDevices, engineCoil) : null;
					var deviceItem = destination == CoilDestination.Playfield && device != null ? GuessDeviceItem(engineCoil, device) : null;

					AddCoil(new CoilMapping {
						Id = engineCoil.Id,
						InternalId = engineCoil.InternalId,
						Description = description,
						Destination = destination,
						Device = device,
						DeviceItem = deviceItem != null ? deviceItem.Id : string.Empty,
						Type = CoilType.SingleWound
					});
				}
			}

			foreach (var holdCoil in holdCoils) {
				var mainCoil = Coils.FirstOrDefault(c => c.Id == holdCoil.MainCoilIdOfHoldCoil);
				if (mainCoil != null) {
					mainCoil.Type = CoilType.DualWound;
					mainCoil.HoldCoilId = holdCoil.Id;

				} else {
					// todo re-think hold coils
					// var playfieldItem = GuessPlayfieldCoil(coils, holdCoil);
					// Data.AddCoil(new MappingsCoilData {
					// 	Id = holdCoil.MainCoilIdOfHoldCoil,
					// 	InternalId = holdCoil.InternalId,
					// 	Description = string.IsNullOrEmpty(holdCoil.Description) ? string.Empty : holdCoil.Description,
					// 	Destination = CoilDestination.Playfield,
					// 	PlayfieldItem = playfieldItem != null ? playfieldItem.Name : string.Empty,
					// 	Type = CoilType.DualWound,
					// 	HoldCoilId = holdCoil.Id
					// });
				}
			}
		}

		private CoilDestination GuessCoilDestination(GamelogicEngineCoil engineCoil)
		{
			if (engineCoil.IsLamp) {
				// todo
				// AddLamp(new LampMapping {
				// 	Id = engineCoil.Id,
				// 	Description = engineCoil.Description,
				// 	Destination = LampDestination.Playfield,
				// 	Source = LampSource.Coils
				// });
				// return CoilDestination.Lamp;
			}
			return CoilDestination.Playfield;
		}

		private static ICoilDeviceAuthoring GuessDevice(ICoilDeviceAuthoring[] coilDevices, GamelogicEngineCoil engineCoil)
		{
			// match by regex if hint provided
			if (!string.IsNullOrEmpty(engineCoil.DeviceHint)) {
				foreach (var device in coilDevices) {
					var regex = new Regex(engineCoil.DeviceHint, RegexOptions.IgnoreCase);
					if (regex.Match(device.name).Success) {
						return device;
					}
				}
			}
			return null;
		}

		private static GamelogicEngineCoil GuessDeviceItem(GamelogicEngineCoil engineCoil, ICoilDeviceAuthoring device)
		{
			if (device.AvailableCoils.Count() == 1) {
				return device.AvailableCoils.First();
			}
			if (!string.IsNullOrEmpty(engineCoil.DeviceItemHint)) {
				foreach (var deviceCoil in device.AvailableCoils) {
					var regex = new Regex(engineCoil.DeviceItemHint, RegexOptions.IgnoreCase);
					if (regex.Match(deviceCoil.Id).Success) {
						return deviceCoil;
					}
				}
			}
			return null;
		}

		/// <summary>
		/// Returns a sorted list of coil names from the gamelogic engine,
		/// appended with the additional names in the coil mapping. In short,
		/// the list of coil names to choose from.
		/// </summary>
		/// <param name="engineCoils">Coil names provided by the gamelogic engine</param>
		/// <returns>All coil names</returns>
		public IEnumerable<GamelogicEngineCoil> GetCoils(GamelogicEngineCoil[] engineCoils)
		{
			var coils = new List<GamelogicEngineCoil>();

			// first, add coils from the gamelogic engine
			if (engineCoils != null) {
				coils.AddRange(engineCoils);
			}

			// then add coil ids that were added manually
			foreach (var coilMapping in Coils) {
				if (!coils.Exists(entry => entry.Id == coilMapping.Id)) {
					coils.Add(new GamelogicEngineCoil(coilMapping.Id));
				}

				if (!string.IsNullOrEmpty(coilMapping.HoldCoilId) && !coils.Exists(entry => entry.Id == coilMapping.HoldCoilId)) {
					coils.Add(new GamelogicEngineCoil(coilMapping.HoldCoilId));
				}
			}

			coils.Sort((s1, s2) => string.Compare(s1.Id, s2.Id, StringComparison.Ordinal));
			return coils;
		}

		public void AddCoil(CoilMapping coilMapping)
		{
			Coils?.Add(coilMapping);
		}

		public void RemoveCoil(CoilMapping coilMapping)
		{
			Coils.Remove(coilMapping);
			// todo
			// if (data.Destination == CoilDestination.Lamp) {
			// 	Lamps = Lamps.Where(l => l.Id == data.Id && l.Source == LampSource.Coils).ToArray();
			// }
		}

		public void RemoveAllCoils()
		{
			Coils.Clear();
			// todo Lamps = Lamps.Where(l => l.Source != LampSource.Coils).ToArray();
		}

		#endregion

		#region Wires

		public void AddWire(WireMapping wireMapping)
		{
			Wires.Add(wireMapping);
		}

		public void RemoveWire(WireMapping wireMapping)
		{
			Wires.Remove(wireMapping);
		}

		public void RemoveAllWires()
		{
			Wires.Clear();
		}

		#endregion

		#region Lamps

				/// <summary>
		/// Auto-matches the lamps provided by the gamelogic engine with the
		/// lamps on the playfield.
		/// </summary>
		/// <param name="engineLamps">List of lamps provided by the gamelogic engine</param>
		/// <param name="tableComponent">Table component</param>
		public void PopulateLamps(GamelogicEngineLamp[] engineLamps, TableAuthoring tableComponent)
		{
			var lamps = tableComponent.GetComponentsInChildren<ILampAuthoring>();
			var gbLamps = new List<GamelogicEngineLamp>();
			foreach (var engineLamp in GetLamps(engineLamps)) {

				var lampMapping = Lamps.FirstOrDefault(mappingsLampData => mappingsLampData.Id == engineLamp.Id && mappingsLampData.Source != LampSource.Coils);
				if (lampMapping != null) {
					continue;
				}

				// we'll handle those in a second loop when all the R lamps are added
				if (!string.IsNullOrEmpty(engineLamp.MainLampIdOfGreen) || !string.IsNullOrEmpty(engineLamp.MainLampIdOfBlue)) {
					gbLamps.Add(engineLamp);
					continue;
				}

				var description = string.IsNullOrEmpty(engineLamp.Description) ? string.Empty : engineLamp.Description;
				var device = GuessLampDevice(lamps, engineLamp);

				AddLamp(new LampMapping {
					Id = engineLamp.Id,
					Description = description,
					Device = device,
					// todo device id
				});
			}

			foreach (var gbLamp in gbLamps) {
				var rLampId = !string.IsNullOrEmpty(gbLamp.MainLampIdOfGreen) ? gbLamp.MainLampIdOfGreen : gbLamp.MainLampIdOfBlue;
				var rLamp = Lamps.FirstOrDefault(c => c.Id == rLampId);
				if (rLamp == null) {
					var device = GuessLampDevice(lamps, gbLamp);
					rLamp = new LampMapping() {
						Id = rLampId,
						Device = device,
						// todo dovice id
					};
					AddLamp(rLamp);
				}

				rLamp.Type = LampType.RgbMulti;
				if (!string.IsNullOrEmpty(gbLamp.MainLampIdOfGreen)) {
					rLamp.Green = gbLamp.Id;

				} else {
					rLamp.Blue = gbLamp.Id;
				}
			}
		}

		/// <summary>
		/// Returns a sorted list of lamp names from the gamelogic engine,
		/// appended with the additional names in the lamp mapping. In short,
		/// the list of lamp names to choose from.
		/// </summary>
		/// <param name="engineLamps">Lamp names provided by the gamelogic engine</param>
		/// <returns>All lamp names</returns>
		public IEnumerable<GamelogicEngineLamp> GetLamps(GamelogicEngineLamp[] engineLamps)
		{
			var lamps = new List<GamelogicEngineLamp>();

			// first, add lamps from the gamelogic engine
			if (engineLamps != null) {
				lamps.AddRange(engineLamps);
			}

			// then add lamp ids that were added manually
			foreach (var lampMapping in Lamps) {
				if (!lamps.Exists(entry => entry.Id == lampMapping.Id)) {
					lamps.Add(new GamelogicEngineLamp(lampMapping.Id));
				}
			}

			lamps.Sort((s1, s2) => s1.Id.CompareTo(s2.Id));
			return lamps;
		}

		private static ILampAuthoring GuessLampDevice(ILampAuthoring[] lamps, GamelogicEngineLamp engineLamp)
		{
			// first, match by regex if hint provided
			if (!string.IsNullOrEmpty(engineLamp.DeviceHint)) {
				foreach (var lamp in lamps) {
					var regex = new Regex(engineLamp.DeviceHint, RegexOptions.IgnoreCase);
					if (regex.Match(lamp.name).Success) {
						return lamp;
					}
				}
			}

			// second, match by "lXX" or name
			var matchKey = int.TryParse(engineLamp.Id, out var numericLampId)
				? $"l{numericLampId}"
				: engineLamp.Id;

			return lamps.FirstOrDefault(l => l.name == matchKey);
		}

		public void AddLamp(LampMapping lampMapping)
		{
			Lamps.Add(lampMapping);
		}

		public void RemoveLamp(LampMapping lampMapping)
		{
			Lamps.Remove(lampMapping);
		}

		public void RemoveAllLamps()
		{
			Lamps.Clear();
		}

		#endregion
	}
}