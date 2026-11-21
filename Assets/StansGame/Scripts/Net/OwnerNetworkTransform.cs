#if STANS_NET
using Unity.Netcode.Components;

namespace StansGame
{
    /// <summary>
    /// Network transform moved by its owner (each player drives their own soldier with no input lag);
    /// everyone else sees it smoothed. World objects use the normal host-driven NetworkTransform.
    /// </summary>
    [UnityEngine.DisallowMultipleComponent]
    public class OwnerNetworkTransform : NetworkTransform
    {
        protected override bool OnIsServerAuthoritative() => false;
    }
}
#endif
