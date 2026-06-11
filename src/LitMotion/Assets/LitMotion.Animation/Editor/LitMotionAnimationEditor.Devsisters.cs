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

        VisualElement CreateComponentPropertyField(SerializedProperty componentProperty, SerializedProperty property)
        {
            if (property.name == "useWorldSpace")
            {
                return CreateUseWorldSpacePropertyField(componentProperty, property);
            }

            return CreateTargetPropertyField(property);
        }

        VisualElement CreateUseWorldSpacePropertyField(SerializedProperty componentProperty, SerializedProperty property)
        {
            var field = new PropertyField(property.Copy());

            // useWorldSpace is ignored for RectTransform targets (they always animate anchoredPosition3D),
            // so disable the field to make that clear. Only applies to position animations.
            if (!IsTransformPositionAnimation(property.GetDeclaredObject()?.GetType()))
            {
                return field;
            }

            var targetProperty = componentProperty.FindPropertyRelative("target");
            if (targetProperty == null)
            {
                return field;
            }

            void UpdateEnabled()
            {
                field.SetEnabled(targetProperty.objectReferenceValue is not RectTransform);
            }

            UpdateEnabled();
            field.TrackPropertyValue(targetProperty.Copy(), _ => UpdateEnabled());

            return field;
        }

        static bool IsTransformPositionAnimation(System.Type type)
        {
            while (type != null)
            {
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Components.TransformPositionAnimationBase<,>))
                {
                    return true;
                }
                type = type.BaseType;
            }
            return false;
        }

        VisualElement CreateAutoPlayModePropertyField(SerializedProperty property)
        {
            var copiedProperty = property.Copy();
            var root = new VisualElement();
            var field = new PropertyField(copiedProperty);
            root.Add(field);

            // OnStart일 때는 경고 HelpBox를 함께 표시한다.
            var warningBox = new HelpBox(
                "OnStart로 설정하면 게임오브젝트가 꺼져도 Tween이 꺼지지 않습니다.",
                HelpBoxMessageType.Warning);
            root.Add(warningBox);

            void UpdateWarningBoxVisible(SerializedProperty currentProperty)
            {
                warningBox.style.display = currentProperty.enumValueIndex == (int)LitMotionAnimation.AutoPlayMode.OnStart
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }

            UpdateWarningBoxVisible(copiedProperty);
            warningBox.TrackPropertyValue(copiedProperty, UpdateWarningBoxVisible);

            // OnStart는 게임오브젝트가 비활성화되어도 Tween이 정지되지 않으므로 의도된 선택인지 확인한다.
            // 콜백 안에서 즉시 프로퍼티를 되돌리면 같은 바인딩 사이클의 write-back이 값을 다시 OnStart로
            // 덮어쓰므로, 다이얼로그와 되돌리기는 바인딩 갱신이 끝난 뒤로 지연시킨다.
            var prevValue = copiedProperty.enumValueIndex;
            var dialogPending = false;
            field.TrackPropertyValue(copiedProperty, changedProperty =>
            {
                var newValue = changedProperty.enumValueIndex;
                if (newValue == prevValue || dialogPending) return;

                if (newValue != (int)LitMotionAnimation.AutoPlayMode.OnStart)
                {
                    prevValue = newValue;
                    return;
                }

                dialogPending = true;
                field.schedule.Execute(() =>
                {
                    dialogPending = false;

                    if (EditorUtility.DisplayDialog(
                        "LitMotion Animation",
                        "OnStart로 설정하면 게임오브젝트가 꺼져도 Tween이 꺼지지 않습니다.\n이걸 의도하신게 맞나요?",
                        "예", "아니오"))
                    {
                        prevValue = (int)LitMotionAnimation.AutoPlayMode.OnStart;
                        return;
                    }

                    // 모달 다이얼로그가 떠 있는 동안 인스펙터가 리바인드되면서 기존 SerializedProperty가
                    // Dispose될 수 있으므로, 보관해 둔 프로퍼티 대신 실행 시점에 새로 조회해서 되돌린다.
                    if (target == null) return;
                    var so = serializedObject;
                    so.Update();
                    var autoPlayModeProperty = so.FindProperty("autoPlayMode");
                    if (autoPlayModeProperty == null) return;
                    autoPlayModeProperty.enumValueIndex = prevValue;
                    so.ApplyModifiedProperties();
                });
            });

            return root;
        }

        VisualElement CreateTargetPropertyField(SerializedProperty property)
        {
            if (property.name != "target")
            {
                return new PropertyField(property);
            }

            var animationComponentType = property.GetDeclaredObject()?.GetType();
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
