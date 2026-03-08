using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Video;
using System;

public class Main : MonoBehaviour {

	public enum Method{
		uniformSpawn,
		circleSpawn,
		discSpawn,
		continuousSpawn,
		areaSpawn
	}

	public enum LCPSolutioner {
		mprgp,
		mprgpmic0,
		psor
	}
	public float epsilon;
	public int solverMaxIterations;
	public LCPSolutioner solver;
	

	public float planeSize;
	

	
	public float agentAvoidanceRadius;
	public float agentMaxSpeed;
	public float agentMinSpeed;
	public bool usePresetGroupDistances;
	public float p1p2, p2p3, p3p4;



	public GameObject agentPrefabs;
	public GameObject groupAgentPrefabs;
	public Agent shirtColorPrefab;


	public Grid gridPrefab;
	public Spawner spawnerPrefab;
	public MapGen mapGen;
	public Plane plane;
	internal static Vector2 xMinMax;
	internal static Vector2 zMinMax;
	internal MapGen.map roadmap;

	public int cellsPerRow;
	public int neighbourBins;
	public int roadNodeAmount; // Number of nodes that are placed automatically
	public bool visibleMap; // Show or hide the nodes in the world
	internal float ringDiameter;

	public bool customTimeStep;
	public float timeStep; 

	[Range(0.01f, 1f)]
	public float alpha; 

	List<Agent> agentList = new List<Agent>();
	public int maxNumberOfAgents = 1000; // Maximum number of agents when spawning continuously

	public bool showSplattedDensity = false;
	public bool showSplattedVelocity = false;
	public bool walkBack = false;
	public bool skipNodeIfSeeNext = false;
	public bool smoothTurns = false;
	public bool handleCollision = false;

	/**
	 * Initialize simulation by taking the user's options into consideration and spawn agents.
	 * Then create the Staggered Grid along with all cells and velocity nodes.
	**/
	void OnEnable () {
		bool error = false; 
		if (error)
			return;
		
		plane.transform.localScale = new Vector3 (planeSize, 1.0f, planeSize);
		Vector3 planeLength = plane.getLengths (); //Staggered grid length
		xMinMax = new Vector2 (plane.transform.position.x - planeLength.x / 2, 
			                   plane.transform.position.x + planeLength.x / 2);
		zMinMax = new Vector2 (plane.transform.position.z - planeLength.z / 2, 
							  plane.transform.position.z + planeLength.z / 2);

		ringDiameter = agentAvoidanceRadius * 2; //Prefered distance between two agents

		//Creates roadmap / pathfinding for agents based on map
		MapGen m = Instantiate (mapGen) as MapGen; 
		roadmap = m.generateRoadMap (roadNodeAmount, xMinMax, zMinMax, visibleMap);


		Grid grid = Instantiate (gridPrefab) as Grid;
		grid.showSplattedDensity = showSplattedDensity;
		grid.showSplattedVelocity = showSplattedVelocity;
		grid.cellsPerRow = cellsPerRow;
		grid.agentMaxSpeed = agentMaxSpeed;
		grid.ringDiameter = ringDiameter;
		grid.usePresetGroupDistances = usePresetGroupDistances;
		grid.groupDistances = new float[] {p1p2, p2p3, p3p4};
		grid.mapGen = mapGen;
		grid.dt = timeStep; 
		grid.neighbourBins = neighbourBins;
		grid.solver = solver;
		grid.solverEpsilon = epsilon;
		grid.solverMaxIterations = solverMaxIterations;
		grid.colHandler = handleCollision;
		grid.agentAvoidanceRadius = agentAvoidanceRadius;
		Grid.instance = grid;
		Grid.instance.initGrid (xMinMax, zMinMax, alpha, agentAvoidanceRadius);

		for (int i = 0; i < roadmap.spawns.Count; ++i)
			roadmap.spawns[i].spawner.InitializeSpawner (ref agentPrefabs, ref groupAgentPrefabs, ref shirtColorPrefab, ref roadmap, 
											 ref agentList, xMinMax, zMinMax, agentAvoidanceRadius);
		
	}
	
	float[] timeNodes = {1f, 3f, 5f, 7f, 9f, 11f, 13f}; 
	HashSet <float> calculatedTime = new HashSet<float>();
	

	/**
	 * Main simulation loop which is called every frame
	**/
	void Update () {
		Grid.instance.solver = solver;
		Grid.instance.solverEpsilon = epsilon;
		Grid.instance.solverMaxIterations = solverMaxIterations;

		// Update grid with new density and velocity values
		Grid.instance.updateCellDensity ();
		Grid.instance.updateVelocityNodes ();
		//Solve linear constraint problem
		Grid.instance.PsolveRenormPsolve ();
		//Move agents
		for (int i = agentList.Count - 1; i >= 0; i--)
		{
			Agent agent = agentList[i];
			if (agent.done)
			{
				Destroy(agent.gameObject);
				agentList.RemoveAt(i);
				continue;
			}
			agent.move(ref roadmap);
		}
		//Pair-wise collision handling between agents
		Grid.instance.collisionHandling(ref agentList);
		
		foreach (float t in timeNodes) {
			if (!calculatedTime.Contains(t) && Time.time >= t) {
				calculatedTime.Add(t);
				Debug.Log("Time: " + t);
				CalculateEntropy();
				Debug.Log("=====================");
			}
			
		}

		//flags
		Grid.instance.showSplattedDensity = showSplattedDensity;
		Grid.instance.showSplattedVelocity = showSplattedVelocity;
		Grid.instance.walkBack = walkBack;
		Grid.instance.skipNodeIfSeeNext = skipNodeIfSeeNext;
		Grid.instance.smoothTurns = smoothTurns;

		Grid.instance.dt = customTimeStep ? timeStep : Time.deltaTime;

	}

	void CalculateEntropy() {
		int totAgents = agentList.Count; //N
		float EN1 = 0;
		float EN2 = 0;
		float EN = 0;
		float alpha_en = 0.5f;
		
		if (totAgents == 0) {
			return;
		}
		
		//En1 direction entropy
		int[] dir = new int[8];
		
		foreach (Agent a in agentList) {
			int intervalDir = (int)(Math.Floor((a.currentDirection) / 45f) % 8);
			dir[intervalDir]++;
        }

		foreach (int numPeople in dir) {
			if (numPeople == 0) continue;
			float x = numPeople / (float)totAgents;
			float eq = x*((float)Math.Log10(x));
			EN1 += eq;
		}

		EN1 = -EN1;
	
		//EN 2 velocity magnitude entropy
		int[] veloMag = new int[8];

		foreach (Agent a in agentList) {
			int intervalVeloMag = Mathf.Min((int)(a.currentSpeed / 0.175f), 7);
			veloMag[intervalVeloMag]++;
		}
		
		foreach (int numPeople in veloMag) {
			if (numPeople == 0) continue;
			float x = numPeople / (float)totAgents;
			float eq = x*((float)Math.Log10(x));
			EN2 += eq;
		}

		EN2 = -EN2;

		EN = (alpha_en * EN1) + (alpha_en * EN2);

		Debug.Log("EN: " + EN);
		Debug.Log("EN1_riktning: " + EN1);
		Debug.Log("EN2_hastighet: " + EN2);
		Debug.Log("Antal agenter: " + totAgents);
	}
}
