using UnityEngine;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Jobs;
using static Unity.Mathematics.math;
using float4x4 = Unity.Mathematics.float4x4;
using quaternion = Unity.Mathematics.quaternion;

public class Fractal : MonoBehaviour
{
	private static readonly int matricesId = Shader.PropertyToID("_Matrices");
	private static MaterialPropertyBlock propertyBlock;


	private static readonly float3[] directions =
	{
		up(), right(), left(), forward(), back()
	};

	private static readonly quaternion[] rotations =
	{
		quaternion.identity,
		quaternion.RotateZ(-0.5f * PI), quaternion.RotateZ(0.5f * PI),
		quaternion.RotateX(0.5f * PI), quaternion.RotateX(-0.5f * PI)
	};

	[SerializeField] [Range(0, 6)] private int depth;
	[SerializeField] private Mesh mesh;
	[SerializeField] private Material material;
	private NativeArray<Matrix4x4>[] matrices;
	private ComputeBuffer[] matricesBuffers;

	private NativeArray<FractalPart>[] parts;

	private void Update()
	{
		float spinAngleDelta = 0.125f * PI * Time.deltaTime;

		var rootPart = parts[0][0];
		rootPart.spinAngle += spinAngleDelta;
		rootPart.worldRotation = mul(transform.rotation,
			mul(rootPart.rotation, quaternion.RotateY(rootPart.spinAngle))
		);
		rootPart.worldPosition = transform.position;
		parts[0][0] = rootPart;
		float objectScale = transform.lossyScale.x;
		matrices[0][0] = float4x4.TRS(
			rootPart.worldPosition, rootPart.worldRotation, float3(objectScale)
		);

		var scale = objectScale;
		JobHandle jobHandle = default;
		for (var li = 1; li < parts.Length; li++)
		{
			scale *= 0.5f;
			jobHandle = new UpdateFractalLevelJob
			{
				spinAngleDelta = spinAngleDelta,
				scale = scale,
				parents = parts[li - 1],
				parts = parts[li],
				matrices = matrices[li]
			}.ScheduleParallel(parts[li].Length, 5, jobHandle);
		}

		jobHandle.Complete();

		var bounds = new Bounds(rootPart.worldPosition, 3f * objectScale * Vector3.one);
		for (var i = 0; i < matricesBuffers.Length; i++)
		{
			var buffer = matricesBuffers[i];
			buffer.SetData(matrices[i]);
			propertyBlock.SetBuffer(matricesId, buffer);
			Graphics.DrawMeshInstancedProcedural(mesh, 0, material, bounds, buffer.count, propertyBlock);
		}
	}

	private void OnEnable()
	{
		parts = new NativeArray<FractalPart>[depth];
		matrices = new NativeArray<Matrix4x4>[depth];
		matricesBuffers = new ComputeBuffer[depth];
		var stride = 16 * 4;
		for (int i = 0, length = 1; i < parts.Length; i++, length *= 5)
		{
			parts[i] = new NativeArray<FractalPart>(length, Allocator.Persistent);
			matrices[i] = new NativeArray<Matrix4x4>(length, Allocator.Persistent);
			matricesBuffers[i] = new ComputeBuffer(length, stride);
		}

		parts[0][0] = CreatePart(0);
		for (var li = 1; li < parts.Length; li++)
		{
			NativeArray<FractalPart> levelParts = parts[li];
			for (var fpi = 0; fpi < levelParts.Length; fpi += 5)
			for (var ci = 0; ci < 5; ci++)
				levelParts[fpi + ci] = CreatePart(ci);
		}

		propertyBlock ??= new MaterialPropertyBlock();
	}

	private void OnDisable()
	{
		for (var i = 0; i < matricesBuffers.Length; i++)
		{
			matricesBuffers[i].Release();
			parts[i].Dispose();
			matrices[i].Dispose();
		}

		parts = null;
		matrices = null;
		matricesBuffers = null;
	}

	private void OnValidate()
	{
		if (parts != null && enabled)
		{
			OnDisable();
			OnEnable();
		}
	}

	private FractalPart CreatePart(int childIndex)
	{
		return new FractalPart
		{
			direction = directions[childIndex],
			rotation = rotations[childIndex]
		};
	}

	[BurstCompile(FloatPrecision.Standard, FloatMode.Fast, CompileSynchronously = true)]
	private struct UpdateFractalLevelJob : IJobFor
	{
		public float spinAngleDelta;
		public float scale;

		[ReadOnly] public NativeArray<FractalPart> parents;
		public NativeArray<FractalPart> parts;

		[WriteOnly] public NativeArray<Matrix4x4> matrices;

		public void Execute(int i)
		{
			FractalPart parent = parents[i / 5];
			FractalPart part = parts[i];
			part.spinAngle += spinAngleDelta;
			part.worldRotation = mul(parent.worldRotation,
				mul(part.rotation, quaternion.RotateY(part.spinAngle))
			);
			part.worldPosition =
				mul(parent.worldRotation, 1.5f * scale * part.direction) + part.worldPosition;
			parts[i] = part;

			matrices[i] = float4x4.TRS(
				part.worldPosition, part.worldRotation, float3(scale)
			);
		}
	}

	private struct FractalPart
	{
		public float3 direction, worldPosition;
		public quaternion rotation, worldRotation;
		public float spinAngle;
	}
}