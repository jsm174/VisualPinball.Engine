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

namespace VisualPinball.Unity
{
	internal static class BumperRingAnimation
	{
		internal static void Update(ref BumperRingAnimationData data, float dTime)
		{
			// todo visibility - skip if invisible

			var limit = data.DropOffset + data.HeightScale * 0.5f;
			if (data.IsHit) {
				data.DoAnimate = true;
				data.AnimateDown = true;
				data.IsHit = false;
			}
			if (data.DoAnimate) {
				var step = data.Speed;
				if (data.AnimateDown) {
					step = -step;
				}
				data.Offset += step * dTime;
				if (data.AnimateDown) {
					if (data.Offset <= -limit) {
						data.Offset = -limit;
						data.AnimateDown = false;
					}
				} else {
					if (data.Offset >= 0.0f) {
						data.Offset = 0.0f;
						data.DoAnimate = false;
					}
				}
			}
		}
	}
}
