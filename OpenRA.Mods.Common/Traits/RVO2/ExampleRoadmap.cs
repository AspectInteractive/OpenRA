﻿/*
 * Roadmap.cc
 * RVO2 Library
 *
 * SPDX-FileCopyrightText: 2008 University of North Carolina at Chapel Hill
 * SPDX-License-Identifier: Apache-2.0
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *  https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 *
 * Please send all bug reports to <geom@cs.unc.edu>.
 *
 * The authors may be contacted via:
 *
 * Jur van den Berg, Stephen J. Guy, Jamie Snape, Ming C. Lin, Dinesh Manocha
 * Dept. of Computer Science
 * 201 S. Columbia St.
 * Frederick P. Brooks, Jr. Computer Science Bldg.
 * Chapel Hill, N.C. 27599-3175
 * United States of America
 *
 * <https://gamma.cs.unc.edu/RVO2/>
 */

/*
 * @file  Roadmap.cc
 * @brief Example file showing a demo with 100 agents split in four groups
 *  initially positioned in four corners of the environment. Each agent
 *  attempts to move to other side of the environment through a narrow
 *  passage generated by four obstacles. There is a roadmap to guide the
 *  agents around the obstacles.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Support;

namespace RVO
{

	public class RoadmapVertex
	{
		public Vector2 position;
		public List<int> neighbors = new();
		public SortedList<int, float> distToGoal = new();
	}

	public class Roadmap
	{
		const float RVO_TWO_PI = 6.28318530717958647692F;
		const int RAND_MAX = 2147483647;

		List<RoadmapVertex> roadmap = new();
		readonly IList<int> goals;
		bool firstRun = true;

		/* Specify the global time step of the simulation. */

		/* Add polygonal obstacles, specifying their vertices in counterclockwise
		 * order.*/
		public List<Vector2> obstacle1 = new();
		public List<Vector2> obstacle2 = new();
		public List<Vector2> obstacle3 = new();
		public List<Vector2> obstacle4 = new();

		public Roadmap()
		{
			goals = new List<int>();
		}

		public void setupScenario()
		{
			Simulator.Instance.setTimeStep(0.25F);

			obstacle1.Add(new Vector2(-10.0F, 40.0F));
			obstacle1.Add(new Vector2(-40.0F, 40.0F));
			obstacle1.Add(new Vector2(-40.0F, 10.0F));
			obstacle1.Add(new Vector2(-10.0F, 10.0F));

			obstacle2.Add(new Vector2(10.0F, 40.0F));
			obstacle2.Add(new Vector2(10.0F, 10.0F));
			obstacle2.Add(new Vector2(40.0F, 10.0F));
			obstacle2.Add(new Vector2(40.0F, 40.0F));

			obstacle3.Add(new Vector2(10.0F, -40.0F));
			obstacle3.Add(new Vector2(40.0F, -40.0F));
			obstacle3.Add(new Vector2(40.0F, -10.0F));
			obstacle3.Add(new Vector2(10.0F, -10.0F));

			obstacle4.Add(new Vector2(-10.0F, -40.0F));
			obstacle4.Add(new Vector2(-10.0F, -10.0F));
			obstacle4.Add(new Vector2(-40.0F, -10.0F));
			obstacle4.Add(new Vector2(-40.0F, -40.0F));

			Simulator.Instance.addObstacle(obstacle1);
			Simulator.Instance.addObstacle(obstacle2);
			Simulator.Instance.addObstacle(obstacle3);
			Simulator.Instance.addObstacle(obstacle4);

			/* Process the obstacles so that they are accounted for in the simulation. */
			Simulator.Instance.processObstacles();

			/* Add roadmap vertices. */
			RoadmapVertex v = new();

			/* Add the goal positions of agents. */
			v.position = new Vector2(-75.0F, -75.0F);
			roadmap.Add(v);
			v.position = new Vector2(75.0F, -75.0F);
			roadmap.Add(v);
			v.position = new Vector2(-75.0F, 75.0F);
			roadmap.Add(v);
			v.position = new Vector2(75.0F, 75.0F);
			roadmap.Add(v);

			/* Add roadmap vertices around the obstacles. */
			v.position = new Vector2(-42.0F, -42.0F);
			roadmap.Add(v);
			v.position = new Vector2(-42.0F, -8.0F);
			roadmap.Add(v);
			v.position = new Vector2(-42.0F, 8.0F);
			roadmap.Add(v);
			v.position = new Vector2(-42.0F, 42.0F);
			roadmap.Add(v);
			v.position = new Vector2(-8.0F, -42.0F);
			roadmap.Add(v);
			v.position = new Vector2(-8.0F, -8.0F);
			roadmap.Add(v);
			v.position = new Vector2(-8.0F, 8.0F);
			roadmap.Add(v);
			v.position = new Vector2(-8.0F, 42.0F);
			roadmap.Add(v);
			v.position = new Vector2(8.0F, -42.0F);
			roadmap.Add(v);
			v.position = new Vector2(8.0F, -8.0F);
			roadmap.Add(v);
			v.position = new Vector2(8.0F, 8.0F);
			roadmap.Add(v);
			v.position = new Vector2(8.0F, 42.0F);
			roadmap.Add(v);
			v.position = new Vector2(42.0F, -42.0F);
			roadmap.Add(v);
			v.position = new Vector2(42.0F, -8.0F);
			roadmap.Add(v);
			v.position = new Vector2(42.0F, 8.0F);
			roadmap.Add(v);
			v.position = new Vector2(42.0F, 42.0F);
			roadmap.Add(v);

			/* Specify the default parameters for agents that are subsequently added. */
			Simulator.Instance.setAgentDefaults(15.0F, 10, 5.0F, 5.0F, 2.0F, 2.0F, default);

			/* Add agents, specifying their start position, and store goals on the
			 * opposite side of the environment (roadmap vertices). */
			for (var i = 0; i < 5; ++i)
			{
				for (var j = 0; j < 5; ++j)
				{
					Simulator.Instance.addAgent(new Vector2(55.0F + i * 10.0F, 55.0F + j * 10.0F));
					goals.Add(0);
					Simulator.Instance.addAgent(new Vector2(-55.0F - i * 10.0F, 55.0F + j * 10.0F));
					goals.Add(1);
					Simulator.Instance.addAgent(new Vector2(55.0F + i * 10.0F, -55.0F - j * 10.0F));
					goals.Add(2);
					Simulator.Instance.addAgent(new Vector2(-55.0F - i * 10.0F, -55.0F - j * 10.0F));
					goals.Add(3);
				}
			}
		}

		void buildRoadmap()
		{
			/* NOLINT(runtime/references) */
			/* Connect the roadmap vertices by edges if mutually visible. */
			for (var i = 0; i < roadmap.Count; ++i)
			{
				for (var j = 0; j < roadmap.Count; ++j)
				{
					if (Simulator.Instance.queryVisibility(roadmap[i].position, roadmap[j].position,
												Simulator.Instance.getAgentRadius(0)))
					{
						roadmap[i].neighbors.Add(j);
					}
				}
			}

			/* Compute the distance to each of the four goals (the first four vertices)
			 * for all vertices using Dijkstra's algorithm. */
			for (var i = 0; i < 4; ++i)
			{
				var Q = new SortedDictionary<float, int>();
				var posInQ = new SortedList<int, SortedDictionary<float, int>.Enumerator>();

				roadmap[i].distToGoal[i] = 0.0F;
				posInQ[i] = Q.GetEnumerator();
				Q.Add(0.0F, i);

				while (Q.Count > 0)
				{
					var u = Q.First().Value;
					Q.Remove(Q.First().Key);
					posInQ[u] = Q.GetEnumerator();

					for (var j = 0; j < roadmap[u].neighbors.Count; ++j)
					{
						var v = roadmap[u].neighbors[j];
						var distUV = RVOMath.abs(roadmap[v].position - roadmap[u].position);

						if (roadmap[v].distToGoal[i] > roadmap[u].distToGoal[i] + distUV)
						{
							roadmap[v].distToGoal[i] = roadmap[u].distToGoal[i] + distUV;

							if (!posInQ[v].MoveNext())
							{
								Q.Add(roadmap[v].distToGoal[i], v);
							}
							else
							{
								Q.Remove(posInQ[v].Current.Key);
								Q.Add(roadmap[v].distToGoal[i], v);
							}
						}
					}
				}
			}
		}

		void setPreferredVelocities()
		{

			/* Set the preferred velocity to be a vector of unit magnitude (speed) in the
			 * direction of the visible roadmap vertex that is on the shortest path to the
			 * goal. */
			for (var i = 0; i < Simulator.Instance.getNumAgents(); ++i)
			{
				var minDist = float.MaxValue;
				var minVertex = -1;

				for (var j = 0; j < roadmap.Count; ++j)
				{
					if (RVOMath.abs(roadmap[j].position - Simulator.Instance.getAgentPosition(i)) +
								roadmap[j].distToGoal[goals[i]] <
							minDist &&
						Simulator.Instance.queryVisibility(Simulator.Instance.getAgentPosition(i),
												   roadmap[j].position,
												   Simulator.Instance.getAgentRadius(i)))
					{
						minDist =
							RVOMath.abs(roadmap[j].position - Simulator.Instance.getAgentPosition(i)) +
							roadmap[j].distToGoal[goals[i]];
						minVertex = j;
					}
				}

				if (minVertex == -1)
				{
					/* No roadmap vertex is visible; should not happen. */
					Simulator.Instance.setAgentPrefVelocity(i, new Vector2(0.0F, 0.0F));
				}
				else
				{
					if (RVOMath.absSq(roadmap[minVertex].position -
								   Simulator.Instance.getAgentPosition(i)) == 0.0F)
					{
						if (minVertex == goals[i])
						{
							Simulator.Instance.setAgentPrefVelocity(i, default);
						}
						else
						{
							Simulator.Instance.setAgentPrefVelocity(
								i, RVOMath.normalize(roadmap[goals[i]].position -
												  Simulator.Instance.getAgentPosition(i)));
						}
					}
					else
					{
						Simulator.Instance.setAgentPrefVelocity(
							i, RVOMath.normalize(roadmap[minVertex].position -
											  Simulator.Instance.getAgentPosition(i)));
					}
				}

				/* Perturb a little to avoid deadlocks due to perfect symmetry. */
				var randGenerator = new MersenneTwister(1);
				var angle = randGenerator.Next(RAND_MAX) * RVO_TWO_PI / RAND_MAX;
				var dist = randGenerator.Next(RAND_MAX) * 0.0001F / RAND_MAX;

				Simulator.Instance.setAgentPrefVelocity(
					i, Simulator.Instance.getAgentPrefVelocity(i) +
						   dist * new Vector2((float)Math.Cos((double)angle), (float)Math.Sin((double)angle)));
			}
		}

		bool reachedGoal()
		{
			/* Check if all agents have reached their goals. */
			for (var i = 0; i < Simulator.Instance.getNumAgents(); ++i)
			{
				if (RVOMath.absSq(Simulator.Instance.getAgentPosition(i) -
							   roadmap[goals[i]].position) > 400.0F)
				{
					return false;
				}
			}

			return true;
		}

		public IEnumerable<Vector2> getAgentPositions()
		{
			if (Simulator.Instance.getNumAgents() == 0)
				yield break;

			for (var i = 0; i < Simulator.Instance.getNumAgents(); ++i)
				yield return Simulator.Instance.getAgentPosition(i);
		}

		public void Tick()
		{
			if (firstRun)
			{
				/* Set up the scenario. */
				setupScenario();
				buildRoadmap();

				firstRun = false;
			}

			/* Perform (and manipulate) the simulation. */
			if (!reachedGoal())
			{
				setPreferredVelocities();
				Simulator.Instance.doStep();
			}
		}
	}
}
