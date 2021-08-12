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

using NLog;
using UnityEditor;
using UnityEngine;
using VisualPinball.Engine.VPT;
using VisualPinball.Engine.VPT.Ramp;
using VisualPinball.Engine.VPT.Surface;
using Logger = NLog.Logger;

namespace VisualPinball.Unity
{
	public static class RampExtensions
	{
		private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

		public static IConvertedItem InstantiateGameObject(this Ramp ramp, IItem item, IMaterialProvider materialProvider)
		{
			var prefab = UnityEngine.Resources.Load<GameObject>("Prefabs/Ramp");
			var obj = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
			obj!.name = item.Name;

			return new ConvertedItem<Ramp, RampData, RampAuthoring>(obj, true);

			// var obj = new GameObject(item.Name);
			// var convertedItem = new ConvertedItem<Ramp, RampData, RampAuthoring>(obj, ramp);
			// switch (ramp.SubComponent) {
			// 	case ItemSubComponent.None:
			// 		convertedItem.SetColliderAuthoring<RampColliderAuthoring>(materialProvider);
			// 		if (ramp.IsHabitrail) {
			// 			convertedItem.AddMeshAuthoring<RampWireMeshAuthoring>(RampMeshGenerator.Wires);
			// 		} else {
			// 			convertedItem.AddMeshAuthoring<RampFloorMeshAuthoring>(RampMeshGenerator.Floor);
			// 			convertedItem.AddMeshAuthoring<RampWallMeshAuthoring>(RampMeshGenerator.Wall);
			// 		}
			// 		break;
			//
			// 	case ItemSubComponent.Collider: {
			// 		convertedItem.SetColliderAuthoring<RampColliderAuthoring>(materialProvider);
			// 		break;
			// 	}
			//
			// 	case ItemSubComponent.Mesh: {
			// 		Logger.Warn("Cannot parent a ramp mesh to a different object than a ramp!");
			// 		break;
			// 	}
			//
			// 	default:
			// 		throw new ArgumentOutOfRangeException();
			// }
			//
			// return convertedItem.AddConvertToEntity();
		}
	}
}