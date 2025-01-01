﻿// Visual Pinball Engine
// Copyright (C) 2023 freezy and VPE Team
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
using System.Collections.Generic;
using System.Linq;
using NativeTrees;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using VisualPinball.Engine.Game;
using VisualPinball.Engine.VPT;
using VisualPinball.Engine.VPT.Flipper;
using Mesh = UnityEngine.Mesh;

namespace VisualPinball.Unity
{
	[DisallowMultipleComponent]
	public abstract class ColliderComponent<TData, TMainComponent> : SubComponent<TData, TMainComponent>, ICollidableComponent
		where TData : ItemData
		where TMainComponent : MainComponent<TData>
	{
		[SerializeReference]
		public PhysicsMaterialAsset PhysicsMaterial;

		[SerializeField]
		public bool ShowColliderMesh;

		[SerializeField]
		public bool ShowColliderOctree;

		[SerializeField]
		public bool ShowAabbs;

		[NonSerialized]
		public int SelectedCollider = -1;

		public bool CollidersDirty { set => _collidersDirty = value; }

		[NonSerialized] private Mesh _transformedColliderMesh;
		[NonSerialized] private Mesh _transformedKinematicColliderMesh;
		[NonSerialized] private Mesh _untransformedColliderMesh;
		[NonSerialized] private Mesh _untransformedKinematicColliderMesh;
		[NonSerialized] private Aabb[] _aabbs;

		[NonSerialized] private readonly List<ICollider> _nonMeshColliders = new List<ICollider>();
		[NonSerialized] private bool _collidersDirty;
		[NonSerialized] private bool _aabbsDirty;

		[NonSerialized] protected PhysicsEngine PhysicsEngine;


		protected abstract IApiColliderGenerator InstantiateColliderApi(Player player, PhysicsEngine physicsEngine);

		public virtual float4x4 GetLocalToPlayfieldMatrixInVpx(float4x4 worldToPlayfield)
			=> Physics.GetLocalToPlayfieldMatrixInVpx(MainComponent.transform.localToWorldMatrix, worldToPlayfield);

		public float4x4 GetUnmodifiedLocalToPlayfieldMatrixInVpx(float4x4 worldToPlayfield)
			=> Physics.GetLocalToPlayfieldMatrixInVpx(MainComponent.transform.localToWorldMatrix, worldToPlayfield);

		public abstract PhysicsMaterialData PhysicsMaterialData { get; }

		private bool HasCachedColliders => false;// _colliderMesh != null && !_collidersDirty;

		private void Start()
		{
			_transformedColliderMesh = null;
			_transformedKinematicColliderMesh = null;
			_untransformedColliderMesh = null;
			_collidersDirty = true;
			// make enable checkbox visible
		}

		private void OnEnable()
		{
			GetComponentInParent<PhysicsEngine>()?.EnableCollider(MainComponent.gameObject.GetInstanceID());
		}

		private void OnDisable()
		{
			GetComponentInParent<PhysicsEngine>()?.DisableCollider(MainComponent.gameObject.GetInstanceID());
		}

		protected PhysicsMaterialData GetPhysicsMaterialData(float elasticity = 1f, float elasticityFalloff = 1f,
			float friction = 0f, float scatterAngleDeg = 0f, bool overwrite = true)
		{
			if (!overwrite && PhysicsMaterial != null) {
				//Debug.Log("load special Physics Material Data:" + PhysicsMaterial.name);
				PhysicsMaterialData physicsMaterialData = new PhysicsMaterialData();
				physicsMaterialData.Elasticity = PhysicsMaterial.Elasticity;
				physicsMaterialData.ElasticityFalloff = PhysicsMaterial.ElasticityFalloff;
				physicsMaterialData.Friction = PhysicsMaterial.Friction;
				physicsMaterialData.ScatterAngleRad = PhysicsMaterial.ScatterAngle;

				physicsMaterialData.ElasticityOverVelocityLUT = PhysicsMaterial.ElasticityOverVelocityLUT;
				physicsMaterialData.UseElasticityOverVelocity = PhysicsMaterial.UseElasticictyOverVelocity;

				return physicsMaterialData;
			}
			return new PhysicsMaterialData {
				Elasticity = elasticity,
				ElasticityFalloff = elasticityFalloff,
				Friction = friction,
				ScatterAngleRad = math.radians(scatterAngleDeg)
			};
		}

		#region Kinematics

		[Tooltip("If set, transforming this object during gameplay will transform the colliders as well.")]
		public bool _isKinematic;

		public bool IsKinematic => _isKinematic;

		/// <summary>
		/// A unique identifier for this item, used in the physics engine to identify items.
		/// </summary>
		public int ItemId => MainComponent.gameObject.GetInstanceID();

		public virtual void OnTransformationChanged(float4x4 currTransformationMatrix)
		{
			// do nothing per default.
		}

		#endregion

		#region Collider Gizmos

#if UNITY_EDITOR

		private Player _player;

		private void OnDrawGizmos()
		{
			if (!_player) {
				_player = GetComponentInParent<Player>();
				if (!_player) {
					return;
				}
			}

			Profiler.BeginSample("ItemColliderComponent.OnDrawGizmosSelected");

			var playfieldColliderComponent = GetComponentInParent<PlayfieldColliderComponent>();
			var showAllColliderMeshes = playfieldColliderComponent && playfieldColliderComponent.ShowAllColliderMeshes;
			var showColliders = ShowColliderMesh || showAllColliderMeshes;

			// early out if nothing to draw
			if (!ShowAabbs && !showColliders && !ShowColliderOctree) {
				Profiler.EndSample();
				return;
			}

			var playfieldToWorld = GetComponentInParent<PlayfieldComponent>().transform.localToWorldMatrix;
			var worldToPlayfield = GetComponentInParent<PlayfieldComponent>().transform.worldToLocalMatrix;
			var localToPlayfieldMatrixInVpx = GetLocalToPlayfieldMatrixInVpx(worldToPlayfield);
			var unmodifiedLocalToPlayfieldMatrixInVpx = GetUnmodifiedLocalToPlayfieldMatrixInVpx(worldToPlayfield);
			var nonTransformableColliderTransforms = new NativeParallelHashMap<int, float4x4>(0, Allocator.Temp);

			var generateColliders = ShowAabbs || showColliders && !HasCachedColliders;
			if (generateColliders) {
				if (Application.isPlaying) {
					InstantiateRuntimeColliders(showColliders);
				} else {
					InstantiateEditorColliders(showColliders, ref nonTransformableColliderTransforms, localToPlayfieldMatrixInVpx);
				}
			}

			if (ShowColliderOctree) {

				var api = InstantiateColliderApi(_player, null);
				var colliders = new ColliderReference(ref nonTransformableColliderTransforms, Allocator.TempJob);
				var kinematicColliders = new ColliderReference(ref nonTransformableColliderTransforms, Allocator.TempJob, true);
				try {
					if (IsKinematic) {
						api.CreateColliders(ref kinematicColliders, localToPlayfieldMatrixInVpx, 0.1f);
					} else {
						api.CreateColliders(ref colliders, localToPlayfieldMatrixInVpx, 0.1f);
					}

					var playfieldBounds = GetComponentInParent<PlayfieldComponent>().Bounds;
					var octree = new NativeOctree<int>(playfieldBounds, 32, 10, Allocator.Persistent);
					var nativeColliders = new NativeColliders(ref colliders, Allocator.TempJob);
					var populateJob = new PhysicsPopulateJob {
						Colliders = nativeColliders,
						Octree = octree,
					};
					populateJob.Run();
					Gizmos.color = Color.yellow;
					octree.DrawGizmos();
				} finally {
					colliders.Dispose();
					kinematicColliders.Dispose();
				}
			}

			// aabbs
			Gizmos.matrix = playfieldToWorld * (Matrix4x4)Physics.VpxToWorld;

			if (showColliders) {

				var white = Color.white;
				white.a = 0.01f;

				if (_untransformedColliderMesh || _untransformedKinematicColliderMesh) {
					Gizmos.matrix = playfieldToWorld * (Matrix4x4)Physics.VpxToWorld * (Matrix4x4)unmodifiedLocalToPlayfieldMatrixInVpx;
					if (_untransformedColliderMesh) {
						Gizmos.color = ColliderColor.UntransformedColliderSelected;
						Gizmos.DrawMesh(_untransformedColliderMesh);
						Gizmos.color = Application.isPlaying ? ColliderColor.UntransformedCollider : white;
						Gizmos.DrawWireMesh(_untransformedColliderMesh);
					}
					if (_untransformedKinematicColliderMesh) {
						Gizmos.color = ColliderColor.UntransformedKineticColliderSelected;
						Gizmos.DrawMesh(_untransformedKinematicColliderMesh);
						Gizmos.color = Application.isPlaying ? ColliderColor.UntransformedKineticCollider : white;
						Gizmos.DrawWireMesh(_untransformedKinematicColliderMesh);
					}
				}

				if (_transformedColliderMesh || _transformedKinematicColliderMesh) {
					Gizmos.matrix = playfieldToWorld * (Matrix4x4)Physics.VpxToWorld;
					if (_transformedColliderMesh) {
						Gizmos.color = ColliderColor.TransformedColliderSelected;
						Gizmos.DrawMesh(_transformedColliderMesh);
						Gizmos.color = Application.isPlaying ? ColliderColor.TransformedCollider : white;
						Gizmos.DrawWireMesh(_transformedColliderMesh);
					}
					if (_transformedKinematicColliderMesh) {
						Gizmos.color = ColliderColor.TransformedKineticColliderSelected;
						Gizmos.DrawMesh(_transformedKinematicColliderMesh);
						Gizmos.color = Application.isPlaying ? ColliderColor.TransformedKineticCollider : white;
						Gizmos.DrawWireMesh(_transformedKinematicColliderMesh);
					}
				}

				DrawNonMeshColliders();
			}

			Gizmos.matrix = Matrix4x4.identity;
			Handles.matrix = Matrix4x4.identity;

			Profiler.EndSample();
		}

		private void InstantiateRuntimeColliders(bool createMesh)
		{
			if (!PhysicsEngine) {
				PhysicsEngine = GetComponentInParent<PhysicsEngine>(); // todo cache
			}

			if (createMesh) {
				if (IsKinematic) {
					var kinematicColliders = PhysicsEngine.GetKinematicColliders(MainComponent.gameObject.GetInstanceID());
					_transformedColliderMesh = null;
					_untransformedColliderMesh = null;
					GenerateColliderMesh(kinematicColliders, out _transformedKinematicColliderMesh, out _untransformedKinematicColliderMesh);
				} else {
					var colliders = PhysicsEngine.GetColliders(MainComponent.gameObject.GetInstanceID());
					_transformedKinematicColliderMesh = null;
					_untransformedKinematicColliderMesh = null;
					GenerateColliderMesh(colliders, out _transformedColliderMesh, out _untransformedColliderMesh);
				}
				_collidersDirty = false;
			}

			// if (ShowAabbs) {
			// 	var count = IsKinematic ? colliders.Length : kinematicColliders.Length;
			// 	var c = IsKinematic ? colliders : kinematicColliders;
			// 	_aabbs = new Aabb[count];
			// 	for (var i = 0; i < count; i++) {
			// 		_aabbs[i] = c[i].Bounds.Aabb;
			// 	}
			// }
		}

		private void InstantiateEditorColliders(bool createMesh, ref NativeParallelHashMap<int, float4x4> nonTransformableColliderTransforms, float4x4 localToPlayfieldMatrixInVpx)
		{
			var api = InstantiateColliderApi(_player, PhysicsEngine);
			var colliders = new ColliderReference(ref nonTransformableColliderTransforms, Allocator.Temp);
			var kinematicColliders = new ColliderReference(ref nonTransformableColliderTransforms, Allocator.Temp, true);
			try {
				if (IsKinematic) {
					api.CreateColliders(ref kinematicColliders, localToPlayfieldMatrixInVpx, 0.1f);
				} else {
					api.CreateColliders(ref colliders, localToPlayfieldMatrixInVpx, 0.1f);
				}

				if (createMesh) {
					if (IsKinematic) {
						_transformedColliderMesh = null;
						_untransformedColliderMesh = null;
						GenerateColliderMesh(ref kinematicColliders, out _transformedKinematicColliderMesh, out _untransformedKinematicColliderMesh);
					} else {
						_transformedKinematicColliderMesh = null;
						_untransformedKinematicColliderMesh = null;
						GenerateColliderMesh(ref colliders, out _transformedColliderMesh, out _untransformedColliderMesh);
					}
					_collidersDirty = false;
				}

				if (ShowAabbs) {
					var count = IsKinematic ? colliders.Count : kinematicColliders.Count;
					var c = IsKinematic ? colliders : kinematicColliders;
					_aabbs = new Aabb[count];
					for (var i = 0; i < count; i++) {
						_aabbs[i] = c[i].Bounds.Aabb;
					}
				}
			} finally {
				colliders.Dispose();
				kinematicColliders.Dispose();
			}
		}

		private void GenerateColliderMesh(ref ColliderReference colliders, out Mesh transformedMesh, out Mesh untransformedMesh)
		{
			var color = Color.green;
			Handles.color = color;
			color.a = 0.3f;
			Gizmos.color = color;
			var vertices = new List<Vector3>();
			var verticesNonTransformable = new List<Vector3>();
			var normals = new List<Vector3>();
			var normalsNonTransformable = new List<Vector3>();
			var indices = new List<int>();
			var indicesNonTransformable = new List<int>();
			_nonMeshColliders.Clear();
			foreach (var col in colliders.CircleColliders) {
				if (col.Header.IsTransformed) {
					AddCollider(col, vertices, normals, indices);
				} else {
					AddCollider(col, verticesNonTransformable, normalsNonTransformable, indicesNonTransformable);
				}
			}
			foreach (var col in colliders.FlipperColliders) {
				if (col.Header.IsTransformed) {
					AddFlipperCollider(vertices, normals, indices, Origin.Global);
				} else {
					AddFlipperCollider(verticesNonTransformable, normalsNonTransformable, indicesNonTransformable, Origin.Original);
				}
			}
			foreach (var col in colliders.GateColliders) {
				if (col.Header.IsTransformed) {
					AddCollider(col.LineSeg0, vertices, normals, indices);
					AddCollider(col.LineSeg1, vertices, normals, indices);
				} else {
					AddCollider(col.LineSeg0, verticesNonTransformable, normalsNonTransformable, indicesNonTransformable);
					AddCollider(col.LineSeg1, verticesNonTransformable, normalsNonTransformable, indicesNonTransformable);
				}
			}
			foreach (var col in colliders.LineColliders) {
				if (col.Header.IsTransformed) {
					AddCollider(col, vertices, normals, indices);
				} else {
					AddCollider(col, verticesNonTransformable, normalsNonTransformable, indicesNonTransformable);
				}
			}
			foreach (var col in colliders.LineSlingshotColliders) {
				if (col.Header.IsTransformed) {
					AddCollider(col, vertices, normals, indices);
				} else {
					AddCollider(col, verticesNonTransformable, normalsNonTransformable, indicesNonTransformable);
				}
			}
			foreach (var col in colliders.LineZColliders) {
				_nonMeshColliders.Add(col);
			}
			foreach (var col in colliders.PlungerColliders) {
				if (col.Header.IsTransformed) {
					AddCollider(col.LineSegBase, vertices, normals, indices);
					AddCollider(col.JointBase0, vertices, normals, indices);
					AddCollider(col.JointBase1, vertices, normals, indices);
				} else {
					AddCollider(col.LineSegBase, verticesNonTransformable, normalsNonTransformable, indicesNonTransformable);
					AddCollider(col.JointBase0, verticesNonTransformable, normalsNonTransformable, indicesNonTransformable);
					AddCollider(col.JointBase1, verticesNonTransformable, normalsNonTransformable, indicesNonTransformable);
				}
			}
			foreach (var col in colliders.SpinnerColliders) {
				if (col.Header.IsTransformed) {
					AddCollider(col.LineSeg0, vertices, normals, indices);
					AddCollider(col.LineSeg1, vertices, normals, indices);
				} else {
					AddCollider(col.LineSeg0, verticesNonTransformable, normalsNonTransformable, indicesNonTransformable);
					AddCollider(col.LineSeg1, verticesNonTransformable, normalsNonTransformable, indicesNonTransformable);
				}
			}
			foreach (var col in colliders.TriangleColliders) {
				if (col.Header.IsTransformed) {
					AddCollider(col, vertices, normals, indices);
				} else {
					AddCollider(col, verticesNonTransformable, normalsNonTransformable, indicesNonTransformable);
				}
			}

			// todo Line3DCollider

			if (vertices.Count > 0) {
				transformedMesh = new Mesh {
					name = $"{name} (static collider)",
					vertices = vertices.ToArray(),
					triangles = indices.ToArray(),
					normals = normals.ToArray()
				};
			} else {
				transformedMesh = null;
			}

			if (verticesNonTransformable.Count > 0) {
				untransformedMesh = new Mesh {
					name = $"{name} (non-transformable colliders)",
					vertices = verticesNonTransformable.ToArray(),
					triangles = indicesNonTransformable.ToArray(),
					normals = normalsNonTransformable.ToArray()
				};
			} else {
				untransformedMesh = null;
			}
		}

		private void GenerateColliderMesh(IEnumerable<ICollider> colliders, out Mesh transformedMesh, out Mesh untransformedMesh)
		{
			var color = Color.magenta;
			Handles.color = color;
			color.a = 0.3f;
			Gizmos.color = color;
			var vertices = new List<Vector3>();
			var verticesNonTransformable = new List<Vector3>();
			var normals = new List<Vector3>();
			var normalsNonTransformable = new List<Vector3>();
			var indices = new List<int>();
			var indicesNonTransformable = new List<int>();
			_nonMeshColliders.Clear();

			foreach (var coll in colliders) {

				switch (coll) {

					// circle collider
					case CircleCollider { Header: { IsTransformed: true } } circleCollider:
						AddCollider(circleCollider, vertices, normals, indices);
						break;
					case CircleCollider circleCollider:
						AddCollider(circleCollider, verticesNonTransformable, normalsNonTransformable, indicesNonTransformable);
						break;

					// flipper collider
					case FlipperCollider { Header: { IsTransformed: true } }:
						AddFlipperCollider(vertices, normals, indices, Origin.Global);
						break;
					case FlipperCollider:
						AddFlipperCollider(verticesNonTransformable, normalsNonTransformable, indicesNonTransformable, Origin.Original);
						break;

					// gate collider
					case GateCollider { Header: { IsTransformed: true } } gateCollider:
						AddCollider(gateCollider.LineSeg0, vertices, normals, indices);
						AddCollider(gateCollider.LineSeg1, vertices, normals, indices);
						break;
					case GateCollider gateCollider:
						AddCollider(gateCollider.LineSeg0, verticesNonTransformable, normalsNonTransformable, indicesNonTransformable);
						AddCollider(gateCollider.LineSeg1, verticesNonTransformable, normalsNonTransformable, indicesNonTransformable);
						break;

					// line collider
					case LineCollider { Header: { IsTransformed: true } } lineCollider:
						AddCollider(lineCollider, vertices, normals, indices);
						break;
					case LineCollider lineCollider:
						AddCollider(lineCollider, verticesNonTransformable, normalsNonTransformable, indicesNonTransformable);
						break;

					// line slingshot collider
					case LineSlingshotCollider { Header: { IsTransformed: true } } lineSlingshotCollider:
						AddCollider(lineSlingshotCollider, vertices, normals, indices);
						break;
					case LineSlingshotCollider lineSlingshotCollider:
						AddCollider(lineSlingshotCollider, verticesNonTransformable, normalsNonTransformable, indicesNonTransformable);
						break;

					// line z collider
					case LineZCollider lineZCollider:
						_nonMeshColliders.Add(lineZCollider);
						break;

					// plunger collider
					case PlungerCollider { Header: { IsTransformed: true } } plungerCollider:
						AddCollider(plungerCollider.LineSegBase, vertices, normals, indices);
						AddCollider(plungerCollider.JointBase0, vertices, normals, indices);
						AddCollider(plungerCollider.JointBase1, vertices, normals, indices);
						break;
					case PlungerCollider plungerCollider:
						AddCollider(plungerCollider.LineSegBase, verticesNonTransformable, normalsNonTransformable, indicesNonTransformable);
						AddCollider(plungerCollider.JointBase0, verticesNonTransformable, normalsNonTransformable, indicesNonTransformable);
						AddCollider(plungerCollider.JointBase1, verticesNonTransformable, normalsNonTransformable, indicesNonTransformable);
						break;

					// spinner collider
					case SpinnerCollider { Header: { IsTransformed: true } } spinnerCollider:
						AddCollider(spinnerCollider.LineSeg0, vertices, normals, indices);
						AddCollider(spinnerCollider.LineSeg1, vertices, normals, indices);
						break;
					case SpinnerCollider spinnerCollider:
						AddCollider(spinnerCollider.LineSeg0, verticesNonTransformable, normalsNonTransformable, indicesNonTransformable);
						AddCollider(spinnerCollider.LineSeg1, verticesNonTransformable, normalsNonTransformable, indicesNonTransformable);
						break;

					// triangle collider
					case TriangleCollider { Header: { IsTransformed: true } } triangleCollider:
						AddCollider(triangleCollider, vertices, normals, indices);
						break;
					case TriangleCollider triangleCollider:
						AddCollider(triangleCollider, verticesNonTransformable, normalsNonTransformable, indicesNonTransformable);
						break;
				}
			}

			// todo Line3DCollider

			if (vertices.Count > 0) {
				transformedMesh = new Mesh {
					name = $"{name} (static collider)",
					vertices = vertices.ToArray(),
					triangles = indices.ToArray(),
					normals = normals.ToArray()
				};
			} else {
				transformedMesh = null;
			}

			if (verticesNonTransformable.Count > 0) {
				untransformedMesh = new Mesh {
					name = $"{name} (static collider)",
					vertices = verticesNonTransformable.ToArray(),
					triangles = indicesNonTransformable.ToArray(),
					normals = normalsNonTransformable.ToArray()
				};
			} else {
				untransformedMesh = null;
			}
		}

		private void DrawNonMeshColliders()
		{
			foreach (var col in _nonMeshColliders) {
				switch (col) {
					case LineZCollider lineZCol: {
						DrawLine(lineZCol.XY.ToFloat3(lineZCol.ZLow), lineZCol.XY.ToFloat3(lineZCol.ZHigh));
							break;
						}
				}
			}
		}

		private static void AddCollider(CircleCollider circleCol, IList<Vector3> vertices, IList<Vector3> normals, ICollection<int> indices)
		{
			var startIdx = vertices.Count;
			const int m_Sides = 32;
			const float angleStep = 360.0f / m_Sides;
			var rotation = Quaternion.Euler(0.0f, 0.0f, angleStep);
			const int max = m_Sides - 1;
			var pos = new Vector3(circleCol.Center.x, circleCol.Center.y, 0);

			// Make first side.
			vertices.Add(rotation * new Vector3(circleCol.Radius, 0f, circleCol.ZHigh) + pos);   // tr
			vertices.Add(rotation * new Vector3(circleCol.Radius, 0f, circleCol.ZLow) + pos);    // bl
			vertices.Add(rotation * (vertices[vertices.Count - 1] - pos) + pos); // br
			vertices.Add(rotation * (vertices[vertices.Count - 3] - pos) + pos); // tl

			// Add triangle indices.
			indices.Add(startIdx + 0);
			indices.Add(startIdx + 1);
			indices.Add(startIdx + 2);
			indices.Add(startIdx + 0);
			indices.Add(startIdx + 2);
			indices.Add(startIdx + 3);

			// Making the first two normals:
			normals.Add((vertices[startIdx] - pos).normalized); // Points "out" of the cylinder.
			normals.Add(normals[startIdx]);
			normals.Add(normals[startIdx]);
			normals.Add(normals[startIdx]);

			// The remaining sides.
			for (var i = 0; i < max; i++) {

				// First vertex.
				vertices.Add(rotation * (vertices[vertices.Count - 2] - pos) + pos);

				indices.Add(vertices.Count - 1); // new vertex
				indices.Add(vertices.Count - 2); // shared
				indices.Add(vertices.Count - 3); // shared

				// Normal: rotate normal from the previous column.
				normals.Add(rotation * normals[normals.Count - 1]);

				// Second vertex.
				vertices.Add(rotation * (vertices[vertices.Count - 2] - pos) + pos);

				indices.Add(vertices.Count - 3); // shared
				indices.Add(vertices.Count - 2); // shared
				indices.Add(vertices.Count - 1); // new vertex

				// Normal: copy previous normal.
				normals.Add(normals[normals.Count - 1]);
			}
		}

		private static void DrawLine(Vector3 p1, Vector3 p2)
		{
			const int thickness = 10;
			Handles.DrawAAPolyLine(thickness, p1, p2);
		}

		private static void AddCollider(LineZCollider lineZCol, ICollection<Vector3> vertices, ICollection<Vector3> normals, ICollection<int> indices)
		{
			const float width = 10f;

			var bottom = lineZCol.XY.ToFloat3(lineZCol.ZLow);
			var top = lineZCol.XY.ToFloat3(lineZCol.ZHigh);

			var i = vertices.Count;
			vertices.Add(bottom + new float3(width, 0, 0));
			vertices.Add(top + new float3(width, 0, 0));
			vertices.Add(top + new float3(-width, 0, 0));
			vertices.Add(bottom + new float3(-width, 0, 0));

			var normal = new Vector3(0, 1, 0);
			normals.Add(normal);
			normals.Add(normal);
			normals.Add(normal);
			normals.Add(normal);

			indices.Add(i + 0);
			indices.Add(i + 2);
			indices.Add(i + 1);
			indices.Add(i + 3);
			indices.Add(i + 2);
			indices.Add(i);
		}

		private static void AddCollider(LineCollider lineCol, ICollection<Vector3> vertices, ICollection<Vector3> normals, ICollection<int> indices)
		{
			AddCollider(
				lineCol.V1, lineCol.V2, lineCol.Normal.ToFloat3(0),
				lineCol.ZLow, lineCol.ZHigh,
				vertices, normals, indices
			);
		}

		private static void AddCollider(LineSlingshotCollider lineCol, ICollection<Vector3> vertices, ICollection<Vector3> normals, ICollection<int> indices)
		{
			AddCollider(
				lineCol.V1, lineCol.V2, lineCol.Normal.ToFloat3(0),
				lineCol.ZLow, lineCol.ZHigh,
				vertices, normals, indices
			);
		}

		private static void AddCollider(float2 v1, float2 v2, float3 normal, float zLow, float zHigh, ICollection<Vector3> vertices, ICollection<Vector3> normals, ICollection<int> indices)
		{
			var h = zHigh - zLow;
			var i = vertices.Count;
			vertices.Add(v1.ToFloat3(zLow));
			vertices.Add(v1.ToFloat3(zLow + h));
			vertices.Add(v2.ToFloat3(zLow));
			vertices.Add(v2.ToFloat3(zLow + h));

			normals.Add(normal);
			normals.Add(normal);
			normals.Add(normal);
			normals.Add(normal);

			indices.Add(i);
			indices.Add(i + 2);
			indices.Add(i + 1);
			indices.Add(i + 2);
			indices.Add(i + 3);
			indices.Add(i + 1);
		}

		private static void AddCollider(TriangleCollider triangleCol, ICollection<Vector3> vertices, ICollection<Vector3> normals, ICollection<int> indices)
		{
			var i = vertices.Count;

			vertices.Add(triangleCol.Rgv0);
			vertices.Add(triangleCol.Rgv1);
			vertices.Add(triangleCol.Rgv2);

			normals.Add(triangleCol.Normal());
			normals.Add(triangleCol.Normal());
			normals.Add(triangleCol.Normal());

			indices.Add(i);
			indices.Add(i + 2);
			indices.Add(i + 1);
		}

		private void AddFlipperCollider(List<Vector3> vertices, List<Vector3> normals, List<int> indices, Origin origin)
		{
			// first see if we already have a mesh
			var flipperComponent = GetComponentInChildren<FlipperComponent>();
			if (flipperComponent == null) {
				return;
			}

			var startIdx = vertices.Count;
			var mesh = new FlipperMeshGenerator(flipperComponent).GetMesh(FlipperMeshGenerator.Rubber, 0, 0.01f, origin, false, 0.2f);
			for (var i = 0; i < mesh.Vertices.Length; i++) {
				var vertex = mesh.Vertices[i];
				vertices.Add(vertex.ToUnityFloat3());
				normals.Add(vertex.ToUnityNormalVector3());
			}
			indices.AddRange(mesh.Indices.Select(n => startIdx + n));
		}

		private static void DrawAabb(Aabb aabb, bool isSelected)
		{
			Gizmos.color = isSelected ? ColliderColor.SelectedAabb : ColliderColor.Aabb;
			Gizmos.DrawWireCube(aabb.Center, aabb.Size);
		}

#endif

		#endregion

		void ICollidableComponent.GetColliders(Player player, PhysicsEngine physicsEngine, ref ColliderReference colliders,
				float4x4 translateWithinPlayfieldMatrix, float margin)
			=> InstantiateColliderApi(player, physicsEngine)
				.CreateColliders(ref colliders, translateWithinPlayfieldMatrix, margin);

		int ICollidableComponent.ItemId => MainComponent.gameObject.GetInstanceID();
		bool ICollidableComponent.IsCollidable => isActiveAndEnabled;
	}

	internal static class ColliderColor
	{
		internal static readonly Color Aabb = new Color32(255, 255, 255, 50);
		internal static readonly Color SelectedAabb = new Color32(255, 255, 255, 128);
		internal static readonly Color TransformedCollider = new Color32(0, 255, 75, 50);
		internal static readonly Color TransformedColliderSelected = new Color32(0, 255, 75, 128);
		internal static readonly Color TransformedKineticCollider = new Color32(255, 255, 0, 50);
		internal static readonly Color TransformedKineticColliderSelected = new Color32(255, 255, 0, 128);
		internal static readonly Color UntransformedCollider = new Color32(0, 255, 255, 50);
		internal static readonly Color UntransformedColliderSelected = new Color32(0, 255, 255, 128);
		internal static readonly Color UntransformedKineticCollider = new Color32(255, 50, 50, 50);
		internal static readonly Color UntransformedKineticColliderSelected = new Color32(255, 50, 50, 128);
	}
}
