using System;
using UnityEngine;
using System.Runtime.InteropServices;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class TelloVideoTexture : MonoBehaviour {

#if (UNITY_IPHONE || UNITY_IOS || UNITY_VISIONOS || UNITY_WEBGL) && !UNITY_EDITOR
	[DllImport ("__Internal")]
#else
	[DllImport("TelloVideoDecoder")]
#endif
	private static extern void SetTextureFromUnity(IntPtr texture, int w, int h);


#if (UNITY_IPHONE || UNITY_IOS || UNITY_VISIONOS || UNITY_WEBGL) && !UNITY_EDITOR
	[DllImport ("__Internal")]
#else
	[DllImport("TelloVideoDecoder")]
#endif
	private static extern IntPtr GetRenderEventFunc();

#if UNITY_WEBGL && !UNITY_EDITOR
	[DllImport ("__Internal")]
	private static extern void RegisterPlugin();
#endif

#if (UNITY_IPHONE || UNITY_IOS || UNITY_VISIONOS || UNITY_WEBGL) && !UNITY_EDITOR
	[DllImport ("__Internal")]
#else
	[DllImport("TelloVideoDecoder")]
#endif
	private static extern void UnityPluginEnable();

#if (UNITY_IPHONE || UNITY_IOS || UNITY_VISIONOS || UNITY_WEBGL) && !UNITY_EDITOR
	[DllImport ("__Internal")]
#else
	[DllImport("TelloVideoDecoder")]
#endif
	private static extern void UnityPluginDisable();

#if (UNITY_IPHONE || UNITY_IOS || UNITY_VISIONOS || UNITY_WEBGL) && !UNITY_EDITOR
	[DllImport ("__Internal")]
#else
	[DllImport("TelloVideoDecoder")]
#endif
	private static extern void PutVideoDataFromUnity([In] byte[] data, int size);

#if (UNITY_IPHONE || UNITY_IOS || UNITY_VISIONOS || UNITY_WEBGL) && !UNITY_EDITOR
	[DllImport ("__Internal")]
#else
	[DllImport("TelloVideoDecoder")]
#endif
	private static extern int TelloVideoDecoderTryGetFrame(IntPtr rgbaBuffer, int bufferSize, out int width, out int height);

#if (UNITY_IPHONE || UNITY_IOS || UNITY_VISIONOS || UNITY_WEBGL) && !UNITY_EDITOR
	[DllImport ("__Internal")]
#else
	[DllImport("TelloVideoDecoder")]
#endif
	private static extern void TelloVideoDecoderGetStatus(
		out int packetCount,
		out int nalCount,
		out int decodeAttemptCount,
		out int decodedFrameCount,
		out int lastDecodeStatus,
		out int lastNalType,
		out int hasSps,
		out int hasPps);

	private const int Width = 1280;
	private const int Height = 720;
	private const TextureFormat TextureFormat_ = TextureFormat.RGBA32;
	private const int MaxQueuedPackets = 240;
	private Texture2D texture;
	private bool editorDecoderWarningShown;
#if UNITY_EDITOR_OSX || (UNITY_VISIONOS && !UNITY_EDITOR)
	private byte[] frameBuffer;
	private GCHandle frameBufferHandle;
	private float lastDecoderStatusLogTime;
#endif
	private readonly Queue<byte[]> pendingVideoPackets = new Queue<byte[]>();
	private readonly object pendingVideoPacketsLock = new object();

	private void Awake()
	{
		EnsureVideoQuad();
	}

	IEnumerator Start()
	{
#if UNITY_WEBGL && !UNITY_EDITOR
		RegisterPlugin();
#endif
#if !UNITY_EDITOR || UNITY_EDITOR_OSX
		UnityPluginEnable();
#endif

		CreateTextureAndPassToPlugin();

		yield return StartCoroutine("CallPluginAtEndOfFrames");
	}

	private void OnApplicationQuit()
	{
#if UNITY_EDITOR_OSX || (UNITY_VISIONOS && !UNITY_EDITOR)
		if (frameBufferHandle.IsAllocated)
			frameBufferHandle.Free();
#endif
#if !UNITY_EDITOR || UNITY_EDITOR_OSX
		UnityPluginDisable();
#endif
	}

	private void Update()
	{
#if !UNITY_EDITOR || UNITY_EDITOR_OSX
		while (true) {
			byte[] packet;

			lock (pendingVideoPacketsLock) {
				if (pendingVideoPackets.Count == 0)
					break;

				packet = pendingVideoPackets.Dequeue();
			}

			PutVideoDataFromUnity(packet, packet.Length);
		}
#else
		DiscardQueuedVideoPacketsInEditor();
#endif

#if UNITY_EDITOR_OSX || (UNITY_VISIONOS && !UNITY_EDITOR)
		TryUploadDecodedFrame();
#endif
	}

	private void CreateTextureAndPassToPlugin()
	{
		texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
		texture.filterMode = FilterMode.Point;
		ClearTexture(Color.black);
		texture.Apply();

		GetComponent<Renderer>().material.mainTexture = texture;

#if !UNITY_EDITOR || UNITY_EDITOR_OSX
		SetTextureFromUnity(texture.GetNativeTexturePtr(), texture.width, texture.height);
#endif

#if UNITY_EDITOR_OSX || (UNITY_VISIONOS && !UNITY_EDITOR)
		frameBuffer = new byte[texture.width * texture.height * 4];
		frameBufferHandle = GCHandle.Alloc(frameBuffer, GCHandleType.Pinned);
#endif
	}

	private IEnumerator CallPluginAtEndOfFrames()
	{
		while (true) {
			yield return new WaitForEndOfFrame();
#if !UNITY_VISIONOS && !UNITY_EDITOR
			GL.IssuePluginEvent(GetRenderEventFunc(), 1);
#endif
		}
	}

	private void EnsureVideoQuad()
	{
		var meshFilter = GetComponent<MeshFilter>();
		if (meshFilter == null)
			meshFilter = gameObject.AddComponent<MeshFilter>();

		if (meshFilter.sharedMesh != null)
			return;

		var mesh = new Mesh {
			name = "Tello Video Quad"
		};

		mesh.vertices = new[] {
			new Vector3(-0.5f, -0.5f, 0f),
			new Vector3(0.5f, -0.5f, 0f),
			new Vector3(-0.5f, 0.5f, 0f),
			new Vector3(0.5f, 0.5f, 0f),
		};
		mesh.uv = new[] {
			new Vector2(0f, 0f),
			new Vector2(1f, 0f),
			new Vector2(0f, 1f),
			new Vector2(1f, 1f),
		};
		mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
		mesh.RecalculateNormals();
		mesh.RecalculateBounds();

		meshFilter.sharedMesh = mesh;
	}

#if UNITY_EDITOR
	private void DiscardQueuedVideoPacketsInEditor()
	{
		var hadPackets = false;

		lock (pendingVideoPacketsLock) {
			hadPackets = pendingVideoPackets.Count > 0;
			pendingVideoPackets.Clear();
		}

		if (hadPackets && !editorDecoderWarningShown) {
			Debug.LogWarning("Tello video packets are being received, but live H.264 video decoding is only enabled in device builds. Build to visionOS/iOS to see the drone video.");
			editorDecoderWarningShown = true;
		}
	}
#endif

	private void ClearTexture(Color color)
	{
		var pixels = new Color32[texture.width * texture.height];
		var clearColor = (Color32)color;

		for (var i = 0; i < pixels.Length; i++)
			pixels[i] = clearColor;

		texture.SetPixels32(pixels);
	}

#if UNITY_EDITOR_OSX || (UNITY_VISIONOS && !UNITY_EDITOR)
	private void TryUploadDecodedFrame()
	{
		if (texture == null || frameBuffer == null || !frameBufferHandle.IsAllocated)
			return;

		var gotFrame = TelloVideoDecoderTryGetFrame(frameBufferHandle.AddrOfPinnedObject(), frameBuffer.Length, out var width, out var height) != 0;
		if (!gotFrame) {
			LogDecoderStatusIfStalled();
			return;
		}

		if (width != texture.width || height != texture.height) {
			texture.Reinitialize(width, height, TextureFormat_, false);
			texture.Apply(false);

			if (frameBufferHandle.IsAllocated)
				frameBufferHandle.Free();

			frameBuffer = new byte[width * height * 4];
			frameBufferHandle = GCHandle.Alloc(frameBuffer, GCHandleType.Pinned);

			gotFrame = TelloVideoDecoderTryGetFrame(frameBufferHandle.AddrOfPinnedObject(), frameBuffer.Length, out width, out height) != 0;
			if (!gotFrame)
				return;
		}

		texture.LoadRawTextureData(frameBuffer);
		texture.Apply(false);
	}

	private void LogDecoderStatusIfStalled()
	{
		if (Time.unscaledTime - lastDecoderStatusLogTime < 2f)
			return;

		lastDecoderStatusLogTime = Time.unscaledTime;

		TelloVideoDecoderGetStatus(
			out var packetCount,
			out var nalCount,
			out var decodeAttemptCount,
			out var decodedFrameCount,
			out var lastDecodeStatus,
			out var lastNalType,
			out var hasSps,
			out var hasPps);

		Debug.LogWarning(
			"Tello decoder has no frame yet. packets=" + packetCount +
			", nals=" + nalCount +
			", decodeAttempts=" + decodeAttemptCount +
			", decodedFrames=" + decodedFrameCount +
			", lastStatus=" + lastDecodeStatus +
			", lastNalType=" + lastNalType +
			", hasSps=" + hasSps +
			", hasPps=" + hasPps);
	}
#endif

	public void PutVideoData(byte[] data)
	{
		if (data == null || data.Length <= 2)
			return;

		if (data.Length > 65535) {
			Debug.LogWarning("Invalid video packet size: " + data.Length);
			return;
		}

		lock (pendingVideoPacketsLock) {
			while (pendingVideoPackets.Count >= MaxQueuedPackets)
				pendingVideoPackets.Dequeue();

			pendingVideoPackets.Enqueue(data);
		}
	}
}
