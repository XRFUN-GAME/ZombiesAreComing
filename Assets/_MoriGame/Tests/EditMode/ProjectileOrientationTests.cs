using System.Reflection;
using MoriGame.Combat;
using MoriGame.Vfx;
using NUnit.Framework;
using UnityEngine;

namespace MoriGame.Tests.EditMode
{
    public class ProjectileOrientationTests
    {
        [Test]
        public void 弹丸前向与起点到终点方向一致()
        {
            var from = new Vector3(1f, 2f, 3f);
            var to = new Vector3(4f, 6f, 8f);

            var rotation = ProjectilePool.FlightRotation(from, to);

            Assert.Less(Quaternion.Angle(Quaternion.LookRotation((to - from).normalized), rotation), 0.001f);
            Assert.Less(Quaternion.Angle(Quaternion.identity,
                ProjectilePool.FlightRotation(from, from)), 0.001f, "零距离弹道保持恒等朝向");
        }

        [Test]
        public void 跟随特效同步宿主位置和旋转()
        {
            var poolObject = new GameObject("FollowOrientationPoolTest");
            var host = new GameObject("FollowOrientationHostTest");
            var prefab = new GameObject("FollowDirectionProbe");
            var library = ScriptableObject.CreateInstance<VfxLibrary>();
            GameObject instance = null;
            try
            {
                prefab.SetActive(false);
                host.transform.SetPositionAndRotation(new Vector3(1f, 2f, 3f),
                    Quaternion.LookRotation(Vector3.right));
                var pool = poolObject.AddComponent<VfxPool>();
                pool.Init(library);

                pool.PlayFollow(new VfxEntry { prefab = prefab, scale = 1f }, host.transform);
                instance = GameObject.Find("FollowDirectionProbe(Clone)");

                Assert.NotNull(instance);
                Assert.Less(Vector3.Distance(host.transform.position, instance.transform.position), 0.0001f);
                Assert.Less(Quaternion.Angle(host.transform.rotation, instance.transform.rotation), 0.001f);

                host.transform.SetPositionAndRotation(new Vector3(4f, 5f, 6f),
                    Quaternion.LookRotation(Vector3.up));
                typeof(VfxPool).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(pool, null);

                Assert.Less(Vector3.Distance(host.transform.position, instance.transform.position), 0.0001f);
                Assert.Less(Quaternion.Angle(host.transform.rotation, instance.transform.rotation), 0.001f);
            }
            finally
            {
                if (instance != null)
                {
                    Object.DestroyImmediate(instance);
                }
                Object.DestroyImmediate(prefab);
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(poolObject);
                Object.DestroyImmediate(library);
            }
        }
    }
}
