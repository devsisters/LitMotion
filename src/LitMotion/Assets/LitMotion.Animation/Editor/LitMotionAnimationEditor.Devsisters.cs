using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor.SceneManagement;

namespace LitMotion.Animation.Editor
{
    public sealed partial class LitMotionAnimationEditor : UnityEditor.Editor
    {
        void SetDefaultTargetObject(SerializedProperty property, System.Type type)
        {
            var targetProperty = property.FindPropertyRelative("target");
            if (targetProperty != null)
            {
                targetProperty.objectReferenceValue = GetDefaultTargetObject(type);
            }
        }

        UnityEngine.Object GetDefaultTargetObject(System.Type animationComponentType)
        {
            if (animationComponentType == null) return null;

            var targetField = ReflectionHelper.GetField(animationComponentType, "target", includingBaseNonPublic: true);
            if (targetField == null) return null;

            var targetType = targetField.FieldType;
            var gameObject = ((LitMotionAnimation)target).gameObject;

            if (targetType == typeof(GameObject))
            {
                return gameObject;
            }

            if (typeof(Component).IsAssignableFrom(targetType) || targetType.IsInterface)
            {
                return gameObject.GetComponent(targetType);
            }

            return null;
        }

        bool CanUseSelf(System.Type animationComponentType)
        {
            if (animationComponentType == null) return false;

            var targetField = ReflectionHelper.GetField(animationComponentType, "target", includingBaseNonPublic: true);
            if (targetField == null) return false;

            var targetType = targetField.FieldType;
            return targetType == typeof(GameObject)
                || typeof(Component).IsAssignableFrom(targetType)
                || targetType.IsInterface;
        }

        VisualElement CreateTargetPropertyField(SerializedProperty property)
        {
            if (property.name != "target")
            {
                return new PropertyField(property);
            }

            var animationComponentType = property.managedReferenceValue?.GetType();
            SerializedProperty copiedProperty = property.Copy();

            var propertyPath = copiedProperty.propertyPath;
            var row = new VisualElement
            {
                style = {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                }
            };

            var field = new PropertyField(copiedProperty)
            {
                style = {
                    flexGrow = 1f,
                }
            };

            var useSelfButton = new Button(() =>
            {
                serializedObject.Update();

                var currentProperty = serializedObject.FindProperty(propertyPath);
                if (currentProperty != null)
                {
                    currentProperty.objectReferenceValue = GetDefaultTargetObject(animationComponentType);
                    serializedObject.ApplyModifiedProperties();
                }
            })
            {
                text = "Use Self",
                style = {
                    width = 76f,
                    marginLeft = 4f,
                }
            };
            useSelfButton.SetEnabled(CanUseSelf(animationComponentType));

            row.Add(field);
            row.Add(useSelfButton);
            return row;
        }
    }
}
