using UnityEngine;
using System.Collections.Generic;
using UnityEngine.UI;
using System;
using ZTools;

namespace Demo
{
    [RequireComponent(typeof(UnityEngine.UI.LoopScrollRect))]
    [DisallowMultipleComponent]
    public class InitOnStart : MonoBehaviour, LoopScrollPrefabSource, LoopScrollDataSource
    {
        public GameObject item;
        public Transform ChoiceBox;
        public int totalCount = -1;
        [HideInInspector]
        public int addVal = -1;
        public int maxValue;
        public int choiceTime;
        public ZCalendar zCalendar;
        [HideInInspector]
        public bool isInit = true;

        // Implement your own Cache Pool here. The following is just for example.
        Stack<Transform> pool = new Stack<Transform>();
        public GameObject GetObject(int index)
        {
            if (pool.Count == 0)
            {
                return Instantiate(item);
            }
            Transform candidate = pool.Pop();
            candidate.gameObject.SetActive(true);
            return candidate.gameObject;
        }

        public void ReturnObject(Transform trans)
        {
            // Use `DestroyImmediate` here if you don't need Pool
            trans.SendMessage("ScrollCellReturn", SendMessageOptions.DontRequireReceiver);
            trans.gameObject.SetActive(false);
            trans.SetParent(transform, false);
            pool.Push(trans);
        }

        public void ProvideData(Transform transform, int idx)
        {
            transform.SendMessage("ScrollCellIndex", idx);
        }

        void Start()
        {
            addVal = -1;
            //if (gameObject.name.ToLower().Contains("hour"))
            //{
            //    addVal = DateTime.Now.Hour - 1;
            //}
            //else if (gameObject.name.ToLower().Contains("min"))
            //{
            //    addVal = DateTime.Now.Minute - 1;
            //}
            //else if (gameObject.name.ToLower().Contains("second"))
            //{
            //    addVal = DateTime.Now.Second - 1;
            //}

            var ls = GetComponent<LoopScrollRect>();
            ls.prefabSource = this;
            ls.dataSource = this;
            ls.totalCount = totalCount;
            ls.RefillCells();
        }
    }
}