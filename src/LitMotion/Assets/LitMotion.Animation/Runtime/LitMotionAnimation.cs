using System;
using System.Collections.Generic;
using LitMotion.Collections;
using UnityEngine;

namespace LitMotion.Animation
{
    [AddComponentMenu("LitMotion Animation")]
    [HelpURL("https://annulusgames.github.io/LitMotion")]
    public sealed class LitMotionAnimation : MonoBehaviour, ISerializationCallbackReceiver
    {
        public enum AutoPlayMode
        {
            None,
            OnStart,
            OnEnable
        }

        public enum AnimationMode
        {
            Parallel,
            Sequential
        }

        // Devsisters Custom: 무한 루프 모션들의 모양을 동기화할 때 사용할 전역 시간 종류.
        public enum GlobalTimeKind
        {
            None,
            Time,
            UnscaledTime,
            Realtime
        }
        // Devsisters Custom

        // Devsisters Custom: AutoPlayMode.OnStart대신 OnEnable를 기본으로 설정
        [SerializeField] AutoPlayMode autoPlayMode = AutoPlayMode.OnEnable;
        // Devsisters Custom

        [SerializeField] AnimationMode animationMode;

        // Devsisters Custom: Parallel 모드에서 무한 루프 모션들의 모양을 동기화하기 위해 재생 시점을 전역 시간으로 맞춘다.
        [SerializeField] GlobalTimeKind globalTimeKind;
        // Devsisters Custom

        [SerializeReference]
        LitMotionAnimationComponent[] components;

        readonly Queue<LitMotionAnimationComponent> queue = new();
        FastListCore<LitMotionAnimationComponent> playingComponents;

        // Devsisters Custom: 새로 추가된 컴포넌트가 version 0으로 인식되어 playOnAwake 마이그레이션이
        // 실행되면 autoPlayMode가 OnStart로 덮어써지므로, 기본값을 마이그레이션 완료 상태로 둔다.
        [HideInInspector, SerializeField] bool playOnAwake = false;
        [HideInInspector, SerializeField] int version = 1;
        // Devsisters Custom

        public IReadOnlyList<LitMotionAnimationComponent> Components => components;

        public AutoPlayMode AutoPlay => autoPlayMode;

        public AnimationMode Animation => animationMode;

        public GlobalTimeKind GlobalTime => globalTimeKind;

        public bool IsStopped { get; private set; }

        /// <summary>
        /// Finds the first animation component of the requested type.
        /// </summary>
        /// <param name="result">The component when found; otherwise null.</param>
        /// <param name="displayName">
        /// Optional display name used to distinguish multiple components of the same type.
        /// </param>
        public bool TryGetAnimationComponent<T>(out T result, string displayName = null)
            where T : LitMotionAnimationComponent
        {
            foreach (var component in components)
            {
                if (component is not T typedComponent) continue;
                if (displayName != null && component.DisplayName != displayName) continue;

                result = typedComponent;
                return true;
            }

            result = null;
            return false;
        }

        void OnEnable()
        {
            if (autoPlayMode == AutoPlayMode.OnEnable)
                Play();
        }

        void Start()
        {
            if (autoPlayMode == AutoPlayMode.OnStart)
                Play();
        }

        void MoveNextMotion()
        {
            if (queue.TryDequeue(out var queuedComponent))
            {
                try
                {
                    var handle = queuedComponent.Play();
                    var isActive = handle.IsActive();

                    if (isActive)
                    {
                        handle.Preserve();
                        MotionManager.GetManagedDataRef(handle, false).OnCompleteAction += MoveNextMotion;

                        // 무한 루프 모션은 완료되지 않아 OnComplete가 발화하지 않으므로, 뒤에 남은 큐의 모션들은 재생되지 못한다.
                        if (handle.Loops < 0 && queue.Count > 0)
                        {
                            Debug.LogError($"An infinitely looping motion ('{queuedComponent.GetType().Name}') was played in a sequential animation. The remaining {queue.Count} motion(s) in the queue will never be played.", this);
                        }
                    }

                    queuedComponent.TrackedHandle = handle;
                    playingComponents.Add(queuedComponent);

                    if (!isActive)
                    {
                        MoveNextMotion();
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex, context: this);
                }
            }
        }

        public void Play()
        {
            IsStopped = false;

            var isPlaying = false;

            foreach (var component in playingComponents.AsSpan())
            {
                var handle = component.TrackedHandle;
                if (handle.IsActive())
                {
                    handle.PlaybackSpeed = 1f;
                    isPlaying = true;

                    component.OnResume();
                }
            }

            if (isPlaying) return;

            playingComponents.Clear();

            switch (animationMode)
            {
                case AnimationMode.Sequential:
                    foreach (var component in components)
                    {
                        if (component == null) continue;
                        if (!component.Enabled) continue;
                        queue.Enqueue(component);
                    }

                    MoveNextMotion();
                    break;
                case AnimationMode.Parallel:
                    foreach (var component in components)
                    {
                        if (component == null) continue;
                        if (!component.Enabled) continue;

                        try
                        {
                            var handle = component.Play();
                            component.TrackedHandle = handle;

                            if (handle.IsActive())
                            {
                                handle.Preserve();

                                // 무한 루프 모션들의 모양을 동기화하기 위해 재생 시점을 전역 시간으로 맞춘다.
                                // 에디트 모드에서는 전역 시간이 의미가 없으므로 재생 중일 때만 적용한다.
                                if (Application.isPlaying && globalTimeKind != GlobalTimeKind.None && handle.Loops < 0)
                                {
                                    handle.Time = GetGlobalTime();
                                }
                            }

                            playingComponents.Add(component);
                        }
                        catch (Exception ex)
                        {
                            Debug.LogException(ex, context: this);
                        }
                    }
                    break;
            }
        }

        float GetGlobalTime()
        {
            return globalTimeKind switch
            {
                GlobalTimeKind.Time => Time.time,
                GlobalTimeKind.UnscaledTime => Time.unscaledTime,
                GlobalTimeKind.Realtime => Time.realtimeSinceStartup,
                _ => 0f
            };
        }

        public void Pause()
        {
            foreach (var component in playingComponents.AsSpan())
            {
                var handle = component.TrackedHandle;
                if (handle.IsActive())
                {
                    handle.PlaybackSpeed = 0f;
                    component.OnPause();
                }
            }
        }

        public void Stop()
        {
            var span = playingComponents.AsSpan();
            span.Reverse();
            foreach (var component in span)
            {
                var handle = component.TrackedHandle;
                handle.TryCancel();
                component.OnStop();
                component.TrackedHandle = handle;
            }

            playingComponents.Clear();
            queue.Clear();

            IsStopped = true;
        }

        /// <summary>
        /// 재생 중인 애니메이션을 즉시 최종 상태로 만든다.
        /// 무한 루프 모션은 최종 상태가 없어 완료시킬 수 없으므로, 에러 로그를 남기고 Cancel로 정리한다.
        /// </summary>
        public void Complete()
        {
            switch (animationMode)
            {
                case AnimationMode.Sequential:
                    // 현재 재생 중인 모션을 완료하면 OnComplete를 통해 MoveNextMotion이 동기적으로 호출되어
                    // 다음 모션이 재생된다. 큐가 비고 활성 모션이 없어질 때까지 반복한다.
                    while (TryGetActivePlayingComponent(out var component))
                    {
                        if (component.TrackedHandle.TryComplete()) continue;

                        // 무한 루프 모션은 완료할 수 없으므로 Cancel로 정리한 뒤 다음 모션으로 진행한다.
                        // (Cancel은 OnComplete를 발화하지 않아 MoveNextMotion이 자동 호출되지 않으므로 직접 호출한다.)
                        CancelInfinitelyLoopingMotion(component);
                        MoveNextMotion();
                    }
                    break;
                case AnimationMode.Parallel:
                    foreach (var component in playingComponents.AsSpan())
                    {
                        var handle = component.TrackedHandle;
                        if (!handle.IsActive()) continue;
                        if (handle.TryComplete()) continue;

                        // 무한 루프 모션은 완료할 수 없으므로 Cancel로 정리한다.
                        CancelInfinitelyLoopingMotion(component);
                    }
                    break;
            }
        }

        void CancelInfinitelyLoopingMotion(LitMotionAnimationComponent component)
        {
            Debug.LogError($"Cannot complete an infinitely looping motion ('{component.GetType().Name}'). Canceling it instead.", this);

            var handle = component.TrackedHandle;
            handle.TryCancel();
            component.OnStop();
            component.TrackedHandle = handle;
        }

        bool TryGetActivePlayingComponent(out LitMotionAnimationComponent result)
        {
            foreach (var component in playingComponents.AsSpan())
            {
                if (component.TrackedHandle.IsActive())
                {
                    result = component;
                    return true;
                }
            }

            result = null;
            return false;
        }

        public void Restart()
        {
            Stop();
            Play();
        }

        public bool IsActive
        {
            get
            {
                if (queue.Count > 0) return true;

                foreach (var component in playingComponents.AsSpan())
                {
                    var handle = component.TrackedHandle;
                    if (handle.IsActive()) return true;
                }

                return false;
            }
        }

        public bool IsPlaying
        {
            get
            {
                if (queue.Count > 0) return true;

                foreach (var component in playingComponents.AsSpan())
                {
                    var handle = component.TrackedHandle;
                    if (handle.IsPlaying()) return true;
                }

                return false;
            }
        }

        void OnDisable()
        {
            if (autoPlayMode == AutoPlayMode.OnEnable)
                Stop();
        }

        void OnDestroy()
        {
            Stop();
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize() { }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            if (version < 1)
            {
                // Devsisters Custom: 자동재생 마이그레이션 결과를 OnStart 대신 OnEnable로 변환
                autoPlayMode = playOnAwake ? AutoPlayMode.OnEnable : AutoPlayMode.None;
                // Devsisters Custom
                version = 1;
            }
        }
    }
}
