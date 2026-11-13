#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace RVFX.MuzzleFlashesImpacts
{
    [CustomEditor(typeof(DecalSpawn_Demo))]
    [CanEditMultipleObjects]
    public sealed class DecalSpawn_DemoEditor : Editor
    {
        private enum Tab
        {
            Aim = 0,
            Firing = 1,
            DecalImpact = 2,
        }

        private static readonly string[] _tabNames =
        {
            "Aim / Rotation",
            "Firing / Spawn",
            "Decal & Impact"
        };

        private Tab _tab;

        private SerializedProperty _gunTransform;
        private SerializedProperty _aimChangeIntervalRange;
        private SerializedProperty _yawRange;
        private SerializedProperty _pitchRange;
        private SerializedProperty _rotateSpeedDegPerSec;

        private SerializedProperty _firingPoint;
        private SerializedProperty _firingEffectPrefab;
        private SerializedProperty _firingIntervalRange;
        private SerializedProperty _firingEffectLifeTime;

        private SerializedProperty _hitLayerMask;
        private SerializedProperty _hitRayDistance;

        private SerializedProperty _spawnDecals;
        private SerializedProperty _decalPrefabs;
        private SerializedProperty _decalNormalOffset;
        private SerializedProperty _decalRollRange;
        private SerializedProperty _decalLifeTime;

        private SerializedProperty _spawnImpacts;
        private SerializedProperty _impactPrefabs;
        private SerializedProperty _impactNormalOffset;
        private SerializedProperty _impactRollRange;
        private SerializedProperty _impactLifeTime;

        private SerializedProperty _useStackedNormalOffsetForDecals;
        private SerializedProperty _useStackedNormalOffsetForImpacts;
        private SerializedProperty _stackedMinOffset;
        private SerializedProperty _stackedMaxOffset;
        private SerializedProperty _stackedStepDistance;

        private void OnEnable()
        {
            _gunTransform = serializedObject.FindProperty("gunTransform");
            _aimChangeIntervalRange = serializedObject.FindProperty("aimChangeIntervalRange");
            _yawRange = serializedObject.FindProperty("yawRange");
            _pitchRange = serializedObject.FindProperty("pitchRange");
            _rotateSpeedDegPerSec = serializedObject.FindProperty("rotateSpeedDegPerSec");

            _firingPoint = serializedObject.FindProperty("firingPoint");
            _firingEffectPrefab = serializedObject.FindProperty("firingEffectPrefab");
            _firingIntervalRange = serializedObject.FindProperty("firingIntervalRange");
            _firingEffectLifeTime = serializedObject.FindProperty("firingEffectLifeTime");

            _hitLayerMask = serializedObject.FindProperty("hitLayerMask");
            _hitRayDistance = serializedObject.FindProperty("hitRayDistance");

            _spawnDecals = serializedObject.FindProperty("spawnDecals");
            _decalPrefabs = serializedObject.FindProperty("decalPrefabs");
            _decalNormalOffset = serializedObject.FindProperty("decalNormalOffset");
            _decalRollRange = serializedObject.FindProperty("decalRollRange");
            _decalLifeTime = serializedObject.FindProperty("decalLifeTime");

            _spawnImpacts = serializedObject.FindProperty("spawnImpacts");
            _impactPrefabs = serializedObject.FindProperty("impactPrefabs");
            _impactNormalOffset = serializedObject.FindProperty("impactNormalOffset");
            _impactRollRange = serializedObject.FindProperty("impactRollRange");
            _impactLifeTime = serializedObject.FindProperty("impactLifeTime");

            _useStackedNormalOffsetForDecals = serializedObject.FindProperty("useStackedNormalOffsetForDecals");
            _useStackedNormalOffsetForImpacts = serializedObject.FindProperty("useStackedNormalOffsetForImpacts");
            _stackedMinOffset = serializedObject.FindProperty("stackedMinOffset");
            _stackedMaxOffset = serializedObject.FindProperty("stackedMaxOffset");
            _stackedStepDistance = serializedObject.FindProperty("stackedStepDistance");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                _tab = (Tab)GUILayout.Toolbar((int)_tab, _tabNames);
            }

            EditorGUILayout.Space(6);

            switch (_tab)
            {
                case Tab.Aim:
                    DrawAimTab();
                    break;
                case Tab.Firing:
                    DrawFiringTab();
                    break;
                case Tab.DecalImpact:
                    DrawDecalImpactTab();
                    break;
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawAimTab()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(_gunTransform, new GUIContent("Gun Transform"));
                EditorGUILayout.PropertyField(_aimChangeIntervalRange, new GUIContent("Aim Change Interval"));
                ClampVector2MinMax(_aimChangeIntervalRange, 0.001f);

                EditorGUILayout.PropertyField(_yawRange, new GUIContent("Yaw Range"));
                EditorGUILayout.PropertyField(_pitchRange, new GUIContent("Pitch Range"));

                EditorGUILayout.PropertyField(_rotateSpeedDegPerSec, new GUIContent("Rotate Speed (Deg/Sec)"));
                if (!_rotateSpeedDegPerSec.hasMultipleDifferentValues && _rotateSpeedDegPerSec.floatValue < 0f)
                    _rotateSpeedDegPerSec.floatValue = 0f;
            }
        }

        private void DrawFiringTab()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(_firingPoint, new GUIContent("Firing Point"));
                EditorGUILayout.PropertyField(_firingEffectPrefab, new GUIContent("Effect Prefab"));

                EditorGUILayout.PropertyField(_firingIntervalRange, new GUIContent("Firing Interval"));
                ClampVector2MinMax(_firingIntervalRange, 0.001f);

                EditorGUILayout.PropertyField(_firingEffectLifeTime, new GUIContent("Effect Lifetime (Sec)"));
                if (!_firingEffectLifeTime.hasMultipleDifferentValues && _firingEffectLifeTime.floatValue < 0f)
                    _firingEffectLifeTime.floatValue = 0f;
            }
        }

        private void DrawDecalImpactTab()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(_hitRayDistance, new GUIContent("Ray Distance"));
                if (!_hitRayDistance.hasMultipleDifferentValues && _hitRayDistance.floatValue < 0.001f)
                    _hitRayDistance.floatValue = 0.001f;

                EditorGUILayout.PropertyField(_hitLayerMask, new GUIContent("Layer Mask"));
            }

            EditorGUILayout.Space(6);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(_useStackedNormalOffsetForDecals, new GUIContent("Stack Offset (Decals)"));
                EditorGUILayout.PropertyField(_useStackedNormalOffsetForImpacts, new GUIContent("Stack Offset (Impacts)"));

                EditorGUILayout.PropertyField(_stackedMinOffset, new GUIContent("Min Offset"));
                EditorGUILayout.PropertyField(_stackedMaxOffset, new GUIContent("Max Offset"));
                EditorGUILayout.PropertyField(_stackedStepDistance, new GUIContent("Step Distance"));

                if (!_stackedStepDistance.hasMultipleDifferentValues && _stackedStepDistance.floatValue < 0.00001f)
                    _stackedStepDistance.floatValue = 0.00001f;
            }

            EditorGUILayout.Space(6);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(_spawnDecals, new GUIContent("Spawn Decals"));

                using (new EditorGUI.DisabledScope(!_spawnDecals.boolValue))
                {
                    EditorGUILayout.PropertyField(_decalPrefabs, new GUIContent("Decal Prefabs"), true);

                    using (new EditorGUI.DisabledScope(_useStackedNormalOffsetForDecals.boolValue))
                    {
                        EditorGUILayout.PropertyField(_decalNormalOffset, new GUIContent("Normal Offset"));
                        if (!_decalNormalOffset.hasMultipleDifferentValues && _decalNormalOffset.floatValue < 0f)
                            _decalNormalOffset.floatValue = 0f;
                    }

                    EditorGUILayout.PropertyField(_decalRollRange, new GUIContent("Random Roll (Deg)"));
                    ClampVector2MinMaxAllowNegative(_decalRollRange);

                    EditorGUILayout.PropertyField(_decalLifeTime, new GUIContent("Decal Lifetime (Sec)"));
                    if (!_decalLifeTime.hasMultipleDifferentValues && _decalLifeTime.floatValue < 0f)
                        _decalLifeTime.floatValue = 0f;
                }
            }

            EditorGUILayout.Space(6);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(_spawnImpacts, new GUIContent("Spawn Impacts"));

                using (new EditorGUI.DisabledScope(!_spawnImpacts.boolValue))
                {
                    EditorGUILayout.PropertyField(_impactPrefabs, new GUIContent("Impact Prefabs"), true);

                    using (new EditorGUI.DisabledScope(_useStackedNormalOffsetForImpacts.boolValue))
                    {
                        EditorGUILayout.PropertyField(_impactNormalOffset, new GUIContent("Normal Offset"));
                        if (!_impactNormalOffset.hasMultipleDifferentValues && _impactNormalOffset.floatValue < 0f)
                            _impactNormalOffset.floatValue = 0f;
                    }

                    EditorGUILayout.PropertyField(_impactRollRange, new GUIContent("Random Roll (Deg)"));
                    ClampVector2MinMaxAllowNegative(_impactRollRange);

                    EditorGUILayout.PropertyField(_impactLifeTime, new GUIContent("Impact Lifetime (Sec)"));
                    if (!_impactLifeTime.hasMultipleDifferentValues && _impactLifeTime.floatValue < 0f)
                        _impactLifeTime.floatValue = 0f;
                }
            }
        }

        private static void ClampVector2MinMax(SerializedProperty vec2Prop, float minClamp)
        {
            if (vec2Prop == null || vec2Prop.propertyType != SerializedPropertyType.Vector2)
                return;

            if (vec2Prop.hasMultipleDifferentValues)
                return;

            Vector2 v = vec2Prop.vector2Value;

            float min = Mathf.Max(minClamp, Mathf.Min(v.x, v.y));
            float max = Mathf.Max(min, Mathf.Max(v.x, v.y));

            vec2Prop.vector2Value = new Vector2(min, max);
        }

        private static void ClampVector2MinMaxAllowNegative(SerializedProperty vec2Prop)
        {
            if (vec2Prop == null || vec2Prop.propertyType != SerializedPropertyType.Vector2)
                return;

            if (vec2Prop.hasMultipleDifferentValues)
                return;

            Vector2 v = vec2Prop.vector2Value;

            float min = Mathf.Min(v.x, v.y);
            float max = Mathf.Max(v.x, v.y);

            vec2Prop.vector2Value = new Vector2(min, max);
        }
    }
}
#endif