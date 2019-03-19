﻿using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

public class BoidJobSystem : JobComponentSystem
{
    [BurstCompile]
    struct SeperationJob: IJobProcessComponentData<Boid, Seperation>
    {
        [ReadOnly]
        public NativeArray<int> neighbours;

        [NativeDisableParallelForRestriction]
        public NativeArray<Vector3> positions;

        public int maxNeighbours;

        public void Execute(ref Boid b, ref Seperation s)
        {
            Vector3 force = Vector3.zero;
            int neighbourStartIndex = maxNeighbours * b.boidId;
            for(int i = 0; i < b.taggedCount; i ++)
            {
                int neighbourId = neighbours[neighbourStartIndex + i];
                Vector3 toNeighbour = positions[b.boidId] - positions[neighbourId];
                force += (Vector3.Normalize(toNeighbour) / toNeighbour.magnitude);
            }
            s.force = force * s.weight;
        }        
    }

    [BurstCompile]
    struct CohesionJob : IJobProcessComponentData<Boid, Cohesion>
    {
        [ReadOnly]
        public NativeArray<int> neighbours;

        [NativeDisableParallelForRestriction]
        public NativeArray<Vector3> positions;

        public int maxNeighbours;

        public void Execute(ref Boid b, ref Cohesion c)
        {
            Vector3 force = Vector3.zero;
            Vector3 centerOfMass = Vector3.zero;
            int neighbourStartIndex = maxNeighbours * b.boidId;
            for (int i = 0; i < b.taggedCount; i++)
            {
                int neighbourId = neighbours[neighbourStartIndex + i];
                centerOfMass += positions[neighbourId];
            }
            if (b.taggedCount > 0)
            {
                centerOfMass /= b.taggedCount;
                // Generate a seek force
                Vector3 toTarget = centerOfMass - positions[b.boidId];
                Vector3 desired = toTarget.normalized * b.maxSpeed;
                force = (desired - b.velocity).normalized;
            }

            c.force = force * c.weight;
        }
    }

    [BurstCompile]
    struct AlignmentJob : IJobProcessComponentData<Boid, Alignment>
    {
        [ReadOnly]
        public NativeArray<int> neighbours;

        [NativeDisableParallelForRestriction]
        public NativeArray<Vector3> positions;

        [NativeDisableParallelForRestriction]
        public NativeArray<Quaternion> rotations;

        public int maxNeighbours;

        public void Execute(ref Boid b, ref Alignment a)
        {
            Vector3 desired = Vector3.zero;
            Vector3 force = Vector3.zero;
            int neighbourStartIndex = maxNeighbours * b.boidId;
            for (int i = 0; i < b.taggedCount; i++)
            {
                int neighbourId = neighbours[neighbourStartIndex + i];
                desired += rotations[neighbourId] * Vector3.forward;
            }
            
            if (b.taggedCount > 0)
            {
                desired /= b.taggedCount;
                force = desired - (rotations[b.boidId] * Vector3.forward);
            }

            a.force = force * a.weight;
        }
    }

    [BurstCompile]
    struct WanderJob : IJobProcessComponentData<Boid, Wander, Position, Rotation>
    {
        public float dT;
        public Unity.Mathematics.Random random;
        public void Execute(ref Boid b, ref Wander w, ref Position p, ref Rotation r)
        {
            Vector3 disp = w.jitter * random.NextFloat3Direction() * dT;
            w.target += disp;
            w.target.Normalize();
            w.target *= w.radius;

            Vector3 localTarget = (Vector3.forward * w.distance) + w.target;

            Quaternion q = r.Value;
            Vector3 pos = p.Value;
            Vector3 worldTarget = (q * localTarget) + pos;
            w.force = (worldTarget - pos) * w.weight;
        }
    }


    [BurstCompile]
    struct BoidJob : IJobProcessComponentData<Boid, Seperation, Alignment, Cohesion, Wander>
    {
        [NativeDisableParallelForRestriction]
        public NativeArray<Vector3> positions;
        
        [NativeDisableParallelForRestriction]
        public NativeArray<Quaternion> rotations;

        public float damping;
        public float banking;

        public float dT;        

        public Vector3 seekTarget;

        public Vector3 Seek(Vector3 target, ref Boid b)
        {
            Vector3 toTarget = target - positions[b.boidId];

            Vector3 desired = toTarget.normalized * b.maxSpeed;
            return desired - b.velocity; 
        }

        public Vector3 AccululateForces(ref Boid b, ref Seperation s, ref Alignment a, ref Cohesion c, ref Wander w)
        {
            Vector3 force = Vector3.zero;

            force += s.force;
            if (force.magnitude >= b.maxForce)
            {
                force = Vector3.ClampMagnitude(force, b.maxForce);
                return force;
            }
            force += a.force;
            if (force.magnitude >= b.maxForce)
            {
                force = Vector3.ClampMagnitude(force, b.maxForce);
                return force;
            }
            
            force += c.force;
            if (force.magnitude >= b.maxForce)
            {
                force = Vector3.ClampMagnitude(force, b.maxForce);
                return force;
            }
            
            force += w.force;
            if (force.magnitude >= b.maxForce)
            {
                force = Vector3.ClampMagnitude(force, b.maxForce);
                return force;
            }
            

            //NativeArray<Vector3> forces;

            //forces = new NativeArray<Vector3>(4, Allocator.Temp);

            //forces[0] = s.force;
            //forces[1] = a.force;
            //forces[2] = c.force;
            //forces[3] = w.force;
            //Vector3 force = Vector3.zero;
            /*
            foreach(Vector3 f in forces)
            {
                force += f;

                float fm = force.magnitude;
                if (fm >= b.maxForce)
                {
                    force = Vector3.ClampMagnitude(force, b.maxForce);
                    break;
                }
            }
            */
            return force;
        }

        public void Execute(ref Boid b, ref Seperation s, ref Alignment a, ref Cohesion c, ref Wander w)
        {
            /*
            Vector3 force = Seek(Vector3.zero, ref  b);
            b.acceleration = force / b.mass;
            b.velocity += b.acceleration * dT;
            positions[b.boidId] += b.velocity * dT;
            if (b.velocity.magnitude > float.Epsilon)
            {
                rotations[b.boidId] = Quaternion.LookRotation(b.velocity.normalized);
            }
            */

            b.force = AccululateForces(ref b, ref s, ref a, ref c, ref w) * b.weight;
            b.force = Vector3.ClampMagnitude(b.force, b.maxForce);
            Vector3 newAcceleration = (b.force * b.weight) / b.mass;
            b.acceleration = Vector3.Lerp(b.acceleration, newAcceleration, dT);
            b.velocity += b.acceleration * dT;

            b.velocity = Vector3.ClampMagnitude(b.velocity, b.maxSpeed);

            if (b.velocity.magnitude > 0)
            {
                Vector3 tempUp = Vector3.Lerp(b.up, Vector3.up + (b.acceleration * banking), dT * 3.0f);
                rotations[b.boidId] = Quaternion.LookRotation(b.velocity, tempUp);
                b.up = rotations[b.boidId] * Vector3.up;

                positions[b.boidId] += b.velocity * dT;
                b.velocity *= (1.0f - (damping * dT));
            }
            b.force = Vector3.zero;
        }
    }

    [BurstCompile]
    struct CopyTransformsToJob:IJobProcessComponentData<Position, Rotation, Boid>
    {
        [NativeDisableParallelForRestriction]
        public NativeArray<Vector3> positions;
        [NativeDisableParallelForRestriction]
        public NativeArray<Quaternion> rotations;

        public void Execute(ref Position p, ref Rotation r, ref Boid b)
        {
            positions[b.boidId] = p.Value;
            rotations[b.boidId] = r.Value;
        }
        
    }

    [BurstCompile]
    struct CopyTransformsFromJob :IJobProcessComponentData<Position, Rotation, Boid>
    {
        [NativeDisableParallelForRestriction]
        public NativeArray<Vector3> positions;

        [NativeDisableParallelForRestriction]
        public NativeArray<Quaternion> rotations;

        public void Execute(ref Position p, ref Rotation r, ref Boid b)
        {
            p.Value = positions[b.boidId];
            r.Value = rotations[b.boidId];
        }        
    }

    [BurstCompile]
    struct CountNeighboursJob : IJobProcessComponentData<Boid>
    {        
        [NativeDisableParallelForRestriction]    
        public NativeArray<int> neighbours;

        [NativeDisableParallelForRestriction]
        public NativeArray<Vector3> positions;
        
        [NativeDisableParallelForRestriction]
        public NativeArray<Quaternion> rotations;

        public float neighbourDistance;
        public int maxNeighbours;
        public void Execute(ref Boid b)
        {
            int neighbourStartIndex = maxNeighbours * b.boidId;
            int neighbourCount = 0;
            for (int i = 0; i < positions.Length; i++)
            {
                if (i != b.boidId)
                {
                    if (Vector3.Distance(positions[b.boidId], positions[i]) < neighbourDistance)
                    {
                        neighbours[neighbourStartIndex + neighbourCount] = i;
                        neighbourCount++;
                        if (neighbourCount == maxNeighbours)
                        {
                            break;
                        }
                    }
                }
            }
            b.taggedCount = neighbourCount;
        }
    }
    
    NativeArray<int> neighbours;
    public NativeArray<Vector3> positions;
    public NativeArray<Quaternion> rotations;

    int maxNeighbours = 50;
    int numBoids;

    Bootstrap bootstrap;

    protected override void OnCreateManager()
    {
        bootstrap = GameObject.FindObjectOfType<Bootstrap>();
        numBoids = bootstrap.numBoids;


        //neighbours = new NativeMultiHashMap<int, int>(10000, Allocator.Persistent);
        neighbours = new NativeArray<int>(numBoids * maxNeighbours, Allocator.Persistent);
        positions = new NativeArray<Vector3>(numBoids, Allocator.Persistent);
        rotations = new NativeArray<Quaternion>(numBoids, Allocator.Persistent);

    }

    protected override void OnDestroyManager()
    {
        neighbours.Dispose();
        positions.Dispose();
        rotations.Dispose();
    }

    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        // Copy entities to the native arrays 
        var ctj = new CopyTransformsToJob()
        {
            positions = this.positions,
            rotations = this.rotations
        };
        var ctjHandle = ctj.Schedule(this, inputDeps);

        // Count Neigthbours
        
        var cnj = new CountNeighboursJob()
        {
            positions = this.positions,
            rotations = this.rotations,
            neighbours = this.neighbours,
            maxNeighbours = this.maxNeighbours,
            neighbourDistance = 10

        };
        var cnjHandle = cnj.Schedule(this, ctjHandle);

        var seperationJob = new SeperationJob()
        {
            positions = this.positions,
            maxNeighbours = this.maxNeighbours,
            neighbours = this.neighbours
        };

        var sjHandle = seperationJob.Schedule(this, cnjHandle);

        var alignmentJob = new AlignmentJob()
        {
            positions = this.positions,
            rotations = this.rotations,
            maxNeighbours = this.maxNeighbours,
            neighbours = this.neighbours
        };

        var ajHandle = alignmentJob.Schedule(this, sjHandle);
        
        var cohesionJob = new CohesionJob()
        {
            positions = this.positions,
            maxNeighbours = this.maxNeighbours,
            neighbours = this.neighbours
        };

        var cjHandle = cohesionJob.Schedule(this, ajHandle);

        var ran = new Unity.Mathematics.Random((uint)UnityEngine.Random.Range(1, 100000));
        var wanderJob = new WanderJob()
        {
            dT = Time.deltaTime,
            random = ran
        };

        var wjHandle = wanderJob.Schedule(this, cjHandle);

        // Integrate the forces
        var boidJob = new BoidJob()
        {
            positions = this.positions,
            rotations = this.rotations,
            dT = Time.deltaTime,
            seekTarget = bootstrap.seekTarget,
            damping = 0.01f,
            banking = 0.1f

        };
        var boidHandle = boidJob.Schedule(this, wjHandle);

        // Copy back to the entities
        var cfj = new CopyTransformsFromJob()
        {
            positions = this.positions,
            rotations = this.rotations
        };
        return cfj.Schedule(this, boidHandle);
    }

    /*
    [Inject] CubeGroup cubeGroup;
    struct CubeGroup
    {
        public int Length;
        ComponentDataArray<Position> positionCDA;
        ComponentDataArray<Rotation> rotationCDA;
        
    }
    */
}