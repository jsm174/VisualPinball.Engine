// Visual Pinball Engine
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

#region ReSharper
// ReSharper disable CompareOfFloatsByEqualityOperator
// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable MemberCanBePrivate.Global
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using VisualPinball.Engine.Game;
using VisualPinball.Engine.VPT.Bumper;

namespace VisualPinball.Unity
{
	[ExecuteAlways]
	[AddComponentMenu("Visual Pinball/Game Item/Bumper")]
	public class BumperAuthoring : ItemMainRenderableAuthoring<Bumper, BumperData>,
		ISwitchAuthoring, ICoilAuthoring, IConvertGameObjectToEntity
	{
		#region Data

		public float Radius = 45f;

		public float Orientation;

		public SurfaceAuthoring Surface;

		#endregion
		protected override Bumper InstantiateItem(BumperData data) => new Bumper(data);

		protected override Type MeshAuthoringType { get; } = typeof(ItemMeshAuthoring<Bumper, BumperData, BumperAuthoring>);
		protected override Type ColliderAuthoringType { get; } = typeof(ItemColliderAuthoring<Bumper, BumperData, BumperAuthoring>);

		public override IEnumerable<Type> ValidParents => BumperColliderAuthoring.ValidParentTypes;

		public ISwitchable Switchable => Item;

		public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
		{
			Convert(entity, dstManager);

			var collComponent = GetComponentInChildren<BumperColliderAuthoring>();
			if (collComponent) {
				dstManager.AddComponentData(entity, new BumperStaticData {
					Force = collComponent.Force,
					HitEvent = collComponent.HitEvent,
					Threshold = collComponent.Threshold
				});
			}

			var table = Table;
			var bumper = Item;

			// add ring data
			var ringAnimComponent = GetComponentInChildren<BumperRingAnimationAuthoring>();
			if (ringAnimComponent) {
				dstManager.AddComponentData(entity, new BumperRingAnimationData {

					// dynamic
					IsHit = false,
					Offset = 0,
					AnimateDown = false,
					DoAnimate = false,

					// static
					DropOffset = ringAnimComponent.RingDropOffset,
					HeightScale = transform.localScale.z,
					Speed = ringAnimComponent.RingSpeed,
					ScaleZ = table.GetScaleZ()
				});
			}

			// add ring data
			if (GetComponentInChildren<BumperSkirtAnimationAuthoring>()) {
				dstManager.AddComponentData(entity, new BumperSkirtAnimationData {
					BallPosition = default,
					AnimationCounter = 0f,
					DoAnimate = false,
					DoUpdate = false,
					EnableAnimation = true,
					Rotation = new float2(0, 0),
					HitEvent = bumper.Data.HitEvent,
					Center = bumper.Data.Center.ToUnityFloat2()
				});
			}

			transform.GetComponentInParent<Player>().RegisterBumper(Item, entity, ParentEntity, gameObject);
		}

		public override void SetData(BumperData data, Dictionary<string, IItemMainAuthoring> itemMainAuthorings)
		{
			transform.localScale = new Vector3(data.Radius, data.Radius, data.HeightScale) / 100f;

			Radius = data.Radius;
			Orientation = data.Orientation;

			Surface = GetAuthoring<SurfaceAuthoring>(itemMainAuthorings, data.Surface);

			var collComponent = GetComponentInChildren<BumperColliderAuthoring>();
			if (collComponent) {
				collComponent.Threshold = data.Threshold;
				collComponent.Force = data.Force;
				collComponent.Scatter = data.Scatter;
				collComponent.HitEvent = data.HitEvent;
			}

			var ringAnimComponent = GetComponentInChildren<BumperRingAnimationAuthoring>();
			if (ringAnimComponent) {
				ringAnimComponent.RingSpeed = data.RingSpeed;
				ringAnimComponent.RingDropOffset = data.RingDropOffset;
			}
		}

		public override void CopyDataTo(BumperData data)
		{
			var localPos = transform.localPosition;

			// name and position
			data.Name = name;
			data.Center = localPos.ToVertex2Dxy();

			// update visibility
			data.IsBaseVisible = false;
			data.IsCapVisible = false;
			data.IsRingVisible = false;
			data.IsSocketVisible = false;
			foreach (var mf in GetComponentsInChildren<MeshFilter>()) {
				switch (mf.sharedMesh.name) {
					case "Bumper Skirt":
						data.IsSocketVisible = mf.gameObject.activeInHierarchy;
						break;
					case "Bumper Base":
						data.IsCapVisible = mf.gameObject.activeInHierarchy;
						break;
					case "Bumper Cap":
						data.IsCapVisible = mf.gameObject.activeInHierarchy;
						break;
					case "Bumper Ring":
						data.IsRingVisible = mf.gameObject.activeInHierarchy;
						break;
				}
			}

			// update collision
			data.IsCollidable = false;
			foreach (var colliderComponent in ColliderComponents) {
				if (colliderComponent is BumperColliderAuthoring colliderAuthoring) {
					data.IsCollidable = colliderAuthoring.gameObject.activeInHierarchy;
				}
			}

			// other props
			data.Radius = Radius;
			data.Orientation = Orientation;

			data.Surface = Surface ? Surface.name : string.Empty;
			data.HeightScale = transform.localScale.z;

			var collComponent = GetComponentInChildren<BumperColliderAuthoring>();
			if (collComponent) {
				data.Threshold = collComponent.Threshold;
				data.Force = collComponent.Force;
				data.Scatter = collComponent.Scatter;
				data.HitEvent = collComponent.HitEvent;
			}

			var ringAnimComponent = GetComponentInChildren<BumperRingAnimationAuthoring>();
			if (ringAnimComponent) {
				data.RingSpeed = ringAnimComponent.RingSpeed;
				data.RingDropOffset = ringAnimComponent.RingDropOffset;
			}
		}

		public override ItemDataTransformType EditorPositionType => ItemDataTransformType.TwoD;
		public override void SetEditorPosition(Vector3 pos) => Data.Center = pos.ToVertex2Dxy();

		public override ItemDataTransformType EditorRotationType => ItemDataTransformType.OneD;
		public override Vector3 GetEditorRotation() => new Vector3(Orientation, 0, 0);
		public override void SetEditorRotation(Vector3 rot) => Orientation = rot.x;

		public override ItemDataTransformType EditorScaleType => ItemDataTransformType.OneD;
		public override Vector3 GetEditorScale() => new Vector3(Data.Radius, 0f, 0f);
		public override void SetEditorScale(Vector3 scale) => Data.Radius = scale.x;
	}
}
