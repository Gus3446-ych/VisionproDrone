using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace PolySpatial.Samples
{
    public class BarycentricMeshData : MonoBehaviour
    {
        [SerializeField]
        ARMeshManager m_MeshManager;

        public ARMeshManager meshManager
        {
            get => m_MeshManager;
            set => m_MeshManager = value;
        }

        [SerializeField]
        BarycentricDataBuilder m_DataBuilder;

        public BarycentricDataBuilder dataBuilder
        {
            get => m_DataBuilder;
            set => m_DataBuilder = value;
        }

#if UNITY_INCLUDE_ARFOUNDATION_6_4_OR_NEWER
        List<MeshUpdateInfo> m_AddedMeshes = new ();
        List<MeshUpdateInfo> m_UpdatedMeshes = new ();
#else
        List<MeshFilter> m_AddedMeshes = new List<MeshFilter>();
        List<MeshFilter> m_UpdatedMeshes = new List<MeshFilter>();
#endif
        void OnEnable()
        {
#if UNITY_INCLUDE_ARFOUNDATION_6_4_OR_NEWER
            m_MeshManager.meshInfosChanged.AddListener(MeshManagerOnMeshesChanged);
#else
            m_MeshManager.meshesChanged += MeshManagerOnMeshesChanged;
#endif
        }

        void OnDisable()
        {
#if UNITY_INCLUDE_ARFOUNDATION_6_4_OR_NEWER
            m_MeshManager.meshInfosChanged.RemoveListener(MeshManagerOnMeshesChanged);
#else
            m_MeshManager.meshesChanged -= MeshManagerOnMeshesChanged;
#endif
        }

#if UNITY_INCLUDE_ARFOUNDATION_6_4_OR_NEWER
        void MeshManagerOnMeshesChanged(ARMeshInfosChangedEventArgs obj)
        {
            m_AddedMeshes.Clear();
            m_UpdatedMeshes.Clear();

            m_AddedMeshes.AddRange(obj.added);
            m_UpdatedMeshes.AddRange(obj.updated);

           foreach (var filter in m_AddedMeshes)
            {
                m_DataBuilder.GenerateData(filter.meshFilter.mesh);
            }

            foreach (var filter in m_UpdatedMeshes)
            {
                m_DataBuilder.GenerateData(filter.meshFilter.sharedMesh);
            }
        }
#else
        void MeshManagerOnMeshesChanged(ARMeshesChangedEventArgs obj)
        {
            m_AddedMeshes = obj.added;
            m_UpdatedMeshes = obj.updated;

            foreach (MeshFilter filter in m_AddedMeshes)
            {
                m_DataBuilder.GenerateData(filter.mesh);
            }

            foreach (MeshFilter filter in m_UpdatedMeshes)
            {
                m_DataBuilder.GenerateData(filter.sharedMesh);
            }
        }
#endif
    }
}
