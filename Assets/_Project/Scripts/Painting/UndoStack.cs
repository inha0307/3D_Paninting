using System;
using System.Collections.Generic;
using UnityEngine;

namespace Wonju.PaintApp
{
    /// <summary>
    /// 캔버스 스냅샷으로 되돌리기·다시하기를 관리합니다. 획 하나 = 스냅샷 하나이며 최대 capacity개를 보관합니다.
    /// 1024px 스냅샷 하나가 4MB이므로 20단계면 GPU 메모리를 약 80MB(다시하기 포함 최대 약 160MB) 씁니다.
    /// </summary>
    public sealed class UndoStack : IDisposable
    {
        readonly int capacity;
        readonly List<RenderTexture> undo = new();
        readonly List<RenderTexture> redo = new();
        readonly Stack<RenderTexture> pool = new();

        public UndoStack(int capacity) => this.capacity = capacity;

        public bool CanUndo => undo.Count > 0;
        public bool CanRedo => redo.Count > 0;
        public event Action Changed;

        /// <summary>바꾸기 직전의 캔버스를 기록합니다. 새 동작이므로 다시하기 기록은 지웁니다.</summary>
        public void Record(RenderTexture canvas)
        {
            undo.Add(Snapshot(canvas));
            if (undo.Count > capacity)
            {
                pool.Push(undo[0]);
                undo.RemoveAt(0);
            }
            ReturnAll(redo);
            Changed?.Invoke();
        }

        public bool Undo(RenderTexture canvas) => Swap(undo, redo, canvas);
        public bool Redo(RenderTexture canvas) => Swap(redo, undo, canvas);

        public void Clear()
        {
            ReturnAll(undo);
            ReturnAll(redo);
            Changed?.Invoke();
        }

        public void Dispose()
        {
            ReturnAll(undo);
            ReturnAll(redo);
            while (pool.Count > 0)
            {
                var rt = pool.Pop();
                rt.Release();
                UnityEngine.Object.Destroy(rt);
            }
        }

        // from의 마지막 상태로 캔버스를 바꾸고, 바뀌기 전 상태를 to에 쌓습니다.
        bool Swap(List<RenderTexture> from, List<RenderTexture> to, RenderTexture canvas)
        {
            if (from.Count == 0) return false;
            to.Add(Snapshot(canvas));
            var last = from[^1];
            from.RemoveAt(from.Count - 1);
            Graphics.CopyTexture(last, canvas);
            pool.Push(last);
            Changed?.Invoke();
            return true;
        }

        RenderTexture Snapshot(RenderTexture canvas)
        {
            var rt = pool.Count > 0 ? pool.Pop() : new RenderTexture(canvas.descriptor) { name = "PaintUndo" };
            if (!rt.IsCreated()) rt.Create();
            Graphics.CopyTexture(canvas, rt);
            return rt;
        }

        void ReturnAll(List<RenderTexture> list)
        {
            foreach (var rt in list) pool.Push(rt);
            list.Clear();
        }
    }
}
