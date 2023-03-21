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

// ReSharper disable ConvertIfStatementToSwitchStatement

using Unity.Collections;
using Unity.Mathematics;
using Unity.Profiling;
using VisualPinball.Unity;

namespace VisualPinballUnity
{
	internal static class PhysicsStaticCollision
	{
		private static readonly ProfilerMarker PerfMarker = new("PhysicsStaticCollision");

		internal static void Collide(float hitTime, ref BallData ball, ref Random random, ref NativeQueue<EventData>.ParallelWriter events)
		{
			
			// find balls with hit objects and minimum time
			if (ball.CollisionEvent.ColliderId < 0 || ball.CollisionEvent.HitTime > hitTime) {
				return;
			}

			PerfMarker.Begin();

			var collEvent = ball.CollisionEvent;
			ref var collider = ref ball.CollisionEvent.Collider;
			Collider.Collide(in collider, ref ball, ref events, ball.Id, in ball.CollisionEvent, ref random);
			ball.CollisionEvent = collEvent;

			// remove trial hit object pointer
			ball.CollisionEvent.ClearCollider();

			PerfMarker.End();
		}
	}
}
