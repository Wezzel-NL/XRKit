using System;

namespace WezzelNL.XRKit.Runtime.Data
{
	[Flags]
	public enum XRDeviceTrackingState : uint
	{
		None = 0,
		Position = 1,
		Rotation = 2,
		Velocity = 4,
		AngularVelocity = 8,
		Acceleration = 16, 
		AngularAcceleration = 32, 
		All = AngularAcceleration | Acceleration | AngularVelocity | Velocity | Rotation | Position,
	}
}