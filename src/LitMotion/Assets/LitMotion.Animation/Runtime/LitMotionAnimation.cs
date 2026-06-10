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
        enum AutoPlayMode
        {
            None,
            OnStart,
            OnEnable
        }

        enum AnimationMode
        {
            Parallel,
            Sequential
        }

        // Devsisters Custom: AutoPlayMode.OnStart대신 OnEnable를 기본으로 설정
        [SerializeField] AutoPlayMode autoPlayMode = AutoPlayMode.OnEnable;
        // Devsisters Custom

        [SerializeField] AnimationMode animationMode;

        [SerializeReference]
        LitMotionAnimationComponent[] components;

        readonly Queue<LitMotionAnimationComponent> queue = new();
        FastListCore<LitMotionAnimationComponent> playingComponents;

        [HideInInspector, SerializeField] bool playOnAwake = true;
        [HideInInspector, SerializeField] int version;

        public IReadOnlyList<LitMotionAnimationComponent> Components => components;

        public bool IsStopped { get; private set; }

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
                autoPlayMode = playOnAwake ? AutoPlayMode.OnStart : AutoPlayMode.None;
                version = 1;
            }
        }
    }
}