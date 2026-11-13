using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MoriGame.Editor
{
    /// <summary>由 TT_Zombies Humanoid 剪辑生成唯一共用 AnimatorController。</summary>
    public static class ZombieAnimatorGenerator
    {
        public const string ControllerPath = "Assets/_MoriGame/Animation/ZombieAnimator.controller";
        private const string ClipRoot = "Assets/ToonyTinyPeople/TT_Zombies/animation/";

        public static RuntimeAnimatorController EnsureController()
        {
            Directory.CreateDirectory("Assets/_MoriGame/Animation");
            if (File.Exists(ControllerPath))
            {
                AssetDatabase.DeleteAsset(ControllerPath);
            }
            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter("Moving", AnimatorControllerParameterType.Bool);
            controller.AddParameter("WalkB", AnimatorControllerParameterType.Bool);
            controller.AddParameter("WalkSpeed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("AttackB", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Damage", AnimatorControllerParameterType.Trigger);

            var states = controller.layers[0].stateMachine;
            var idle = states.AddState("Idle", new Vector3(280f, 0f, 0f));
            idle.motion = Clip("Z_dumb_idle_A");
            var walkA = states.AddState("WalkA", new Vector3(520f, -80f, 0f));
            walkA.motion = Clip("Z_dumb_walk_A");
            var walkB = states.AddState("WalkB", new Vector3(520f, 80f, 0f));
            walkB.motion = Clip("Z_dumb_walk_B");
            var attackA = states.AddState("AttackA", new Vector3(760f, -160f, 0f));
            attackA.motion = Clip("Z_attack_A");
            var attackB = states.AddState("AttackB", new Vector3(760f, -60f, 0f));
            attackB.motion = Clip("Z_attack_B");
            var damage = states.AddState("Damage", new Vector3(760f, 60f, 0f));
            damage.motion = Clip("Z_damage");
            var death = states.AddState("Death", new Vector3(760f, 180f, 0f));
            death.motion = Clip("Death/Z_death_A");
            states.defaultState = idle;
            SetWalkSpeed(walkA);
            SetWalkSpeed(walkB);

            Transition(idle, walkA, false, "Moving", true, "WalkB", false);
            Transition(idle, walkB, false, "Moving", true, "WalkB", true);
            Transition(walkA, idle, false, "Moving", false);
            Transition(walkB, idle, false, "Moving", false);
            Transition(walkA, walkB, false, "WalkB", true);
            Transition(walkB, walkA, false, "WalkB", false);

            AnyState(states, damage, "Damage");
            var attackATransition = AnyState(states, attackA, "Attack");
            attackATransition.AddCondition(AnimatorConditionMode.IfNot, 0f, "AttackB");
            var attackBTransition = AnyState(states, attackB, "Attack");
            attackBTransition.AddCondition(AnimatorConditionMode.If, 0f, "AttackB");

            ExitTo(attackA, idle);
            ExitTo(attackB, idle);
            ExitTo(damage, walkA, "Moving", true, "WalkB", false);
            ExitTo(damage, walkB, "Moving", true, "WalkB", true);
            ExitTo(damage, idle, "Moving", false);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        private static void SetWalkSpeed(AnimatorState state)
        {
            state.speedParameterActive = true;
            state.speedParameter = "WalkSpeed";
        }

        private static AnimatorStateTransition Transition(AnimatorState from, AnimatorState to,
            bool hasExitTime, string boolName, bool expected)
        {
            var transition = from.AddTransition(to);
            transition.hasExitTime = hasExitTime;
            transition.duration = 0.12f;
            transition.AddCondition(expected ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot,
                0f, boolName);
            return transition;
        }

        private static AnimatorStateTransition Transition(AnimatorState from, AnimatorState to,
            bool hasExitTime, string boolName, bool expected, string secondBool, bool secondExpected)
        {
            var transition = Transition(from, to, hasExitTime, boolName, expected);
            transition.AddCondition(secondExpected ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot,
                0f, secondBool);
            return transition;
        }

        private static AnimatorStateTransition AnyState(AnimatorStateMachine states,
            AnimatorState to, string trigger)
        {
            var transition = states.AddAnyStateTransition(to);
            transition.hasExitTime = false;
            transition.duration = 0.08f;
            transition.canTransitionToSelf = false;
            transition.AddCondition(AnimatorConditionMode.If, 0f, trigger);
            return transition;
        }

        private static AnimatorStateTransition ExitTo(AnimatorState state, AnimatorState destination,
            string boolName = null, bool expected = false, string secondBool = null, bool secondExpected = false)
        {
            var transition = state.AddTransition(destination);
            transition.hasExitTime = true;
            transition.exitTime = 0.95f;
            transition.duration = 0.1f;
            if (!string.IsNullOrEmpty(boolName))
            {
                transition.AddCondition(expected ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot,
                    0f, boolName);
            }
            if (!string.IsNullOrEmpty(secondBool))
            {
                transition.AddCondition(secondExpected ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot,
                    0f, secondBool);
            }
            return transition;
        }

        private static AnimationClip Clip(string name)
        {
            var sourcePath = ClipRoot + name + ".FBX";
            var imported = AssetDatabase.LoadAssetAtPath<AnimationClip>(sourcePath);
            if (imported == null)
            {
                Debug.LogError("[MoriGame] 缺少僵尸动画剪辑 " + sourcePath);
                return null;
            }
            var directory = "Assets/_MoriGame/Animation/Clips";
            Directory.CreateDirectory(directory);
            var path = directory + "/" + name.Replace('/', '_') + ".anim";
            var standalone = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (standalone != null)
            {
                return standalone;
            }
            standalone = UnityEngine.Object.Instantiate(imported);
            standalone.name = name.Replace('/', '_');
            AssetDatabase.CreateAsset(standalone, path);
            return standalone;
        }
    }
}
