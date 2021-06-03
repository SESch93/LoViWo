using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using MathNet.Numerics.LinearAlgebra;
using static JSONManager;

public static class KnowledgeBasedSimulation
{
    public static void startKnowledgeBasedSimulation(Dictionary<Fact, float> knownAvMap) {
        List<SimplifiedFact> sFactList = GameState.LastKBSimulationResult.Item2;

        //Only send server-request if global fact-list has changed
        if (!GameState.LastKBSimulationResult.Item1.SequenceEqual(GameState.Facts))
        {
            if (GameState.ServerRunning)
            {
                CogwheelEqsysFact eqsysFact = addEqsysFact();
                List<SimplifiedFact> response = listSimplifiedFacts();

                if (response != null)
                {
                    GameState.LastKBSimulationResult = new Tuple<List<Fact>, List<SimplifiedFact>>(GameState.Facts, response);
                    sFactList = response;
                }
                else {
                    Debug.Log("KnowledgeBasedSimulation: /fact/list reponse is null. Using LastKBSimuationResult.");
                }
            }
            else
            {
                Debug.LogWarning("KnowledgeBasedSimulation: Cannot send server-request, because FrameIT-Server is not running.");
            }
        }

        if (sFactList != null)
        {
            List<SEqsysFact> sEqsysFacts = sFactList.FindAll(sFact => sFact.GetType().Equals(typeof(SEqsysFact)))
                                                    .Select(sFact => (SEqsysFact) sFact)
                                                    .ToList();
            SEqsysFact eqsysFactForSimulation = null;

            if (sEqsysFacts.Count == 0)
                Debug.Log("KnowledgeBasedSimulation: sFactList contains no SEqsysFact.");
            else if (sEqsysFacts.Count > 1)
            {
                Debug.Log("KnowledgeBasedSimulation: sFactList contains more than one SEqsysFact. Using newest one for Simulation.");
                eqsysFactForSimulation = sEqsysFacts.ElementAt(sEqsysFacts.Count - 1);
            }
            else
                eqsysFactForSimulation = sEqsysFacts.ElementAt(0);

            if (eqsysFactForSimulation != null) {
                //Prepare Data (parse equations) for gls-solver
                Tuple<List<List<double>>, List<double>, List<MMTTerm>> glsTuple = eqsysFactForSimulation.parseEquationSystem();

                if (glsTuple == null)
                {
                    Debug.Log("KnowledgeBasedSimulation: Sth. went wrong while parsing the EquationSystem.");
                }
                else {
                    List<List<double>> AData = glsTuple.Item1;
                    List<double> bData = glsTuple.Item2;
                    List<MMTTerm> variables = glsTuple.Item3;

                    addKnownEqsysValues(AData, bData, variables, knownAvMap);

                    Matrix<double> A = Matrix<double>.Build.DenseOfRows(AData);
                    Vector<double> b = Vector<double>.Build.DenseOfEnumerable(bData);
                    //Solve GLS of the form 'A * x = b':
                    Vector<double> glsSolution = A.Solve(b);
                    
                    //Map the glsSolution to the variables and to the corresponding cogwheels
                    Dictionary<Fact, float> newlyDiscoveredAvsMap = getNewlyDiscoveredAvsMap(knownAvMap, variables, glsSolution);
                    
                    if (newlyDiscoveredAvsMap != null)
                    {
                        //Use result to rotate cogwheels (and their connected parts)
                        foreach (KeyValuePair<Fact, float> newlyDiscoveredAv in newlyDiscoveredAvsMap) {
                            RotatableCogwheel rcComponent = newlyDiscoveredAv.Key.Representation.GetComponentInChildren<RotatableCogwheel>();
                            if (rcComponent != null)
                            {
                                rcComponent.rotate(newlyDiscoveredAv.Value);
                            }
                            else
                                Debug.Log("KnowledgeBasedSimulation.startKnowledgeBasedSimulation: Didn't find RotatableCogwheel component" +
                                    "in newlyDiscoveredAv.");
                        }
                    }
                }
            }
        }
        else {
            Debug.Log("KnowledgeBasedSimulation: Cannot simulate, sFactList is null.");
        }
    }

    private static CogwheelEqsysFact addEqsysFact() {
        int eqsysFactId = GameState.Facts.Count;
        int[] cogIds = GameState.Facts.FindAll(fact => fact.GetType().Equals(typeof(CogwheelFact))).Select(fact => fact.Id).ToArray();
        CogwheelEqsysFact eqsys = new CogwheelEqsysFact(eqsysFactId, cogIds);
        GameState.Facts.Insert(eqsysFactId, eqsys);
        return eqsys;
    }

    private static List<SimplifiedFact> listSimplifiedFacts() {
        UnityWebRequest request = UnityWebRequest.Get(GameSettings.ServerAdress + "/fact/list");
        request.method = UnityWebRequest.kHttpVerbGET;
        AsyncOperation op = request.SendWebRequest();
        while (!op.isDone) { }
        if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError)
        {
            Debug.LogWarning(request.error);
            return null;
        }
        else
        {
            string response = request.downloadHandler.text;
            Debug.Log("KnowledgeBasedSimulation: Json-Response from /fact/list-endpoint: " + response);
            return SimplifiedFact.FromJSON(response);
        }
    }

    private static void addKnownEqsysValues(List<List<double>> AData, List<double> bData, List<MMTTerm> variables, Dictionary<Fact, float> knownAvMap) {
        //Add a row in AData and a value in bData for the known angularVelocity(s) of the rotating cogwheel(s)
        foreach (KeyValuePair<Fact, float> knownAv in knownAvMap)
        {
            if (knownAv.Key.GetType().Equals(typeof(CogwheelFact)))
            {
                //Find Variable, which represents the unknown angularVelocity of a cogwheel, and which is equal
                //to a cogwheel whose angularVelocity is already known (Because it is the rotatingCogwheel driven by the generator).
                MMTTerm knownAvVariable = variables.Find(variable => variable.isSimplifiedCogwheelAvTerm()
                                                                        && ((OMF)((RECARG)((OMA)((OMA)variable).arguments.ElementAt(0)).arguments.ElementAt(0)).value).f.Equals(((CogwheelFact)knownAv.Key).Radius)
                                                                        && ((OMF)((OMA)((RECARG)((OMA)((OMA)variable).arguments.ElementAt(0)).arguments.ElementAt(1)).value).arguments.ElementAt(0)).f.Equals(((CogwheelFact)knownAv.Key).Point.x)
                                                                        && ((OMF)((OMA)((RECARG)((OMA)((OMA)variable).arguments.ElementAt(0)).arguments.ElementAt(1)).value).arguments.ElementAt(1)).f.Equals(((CogwheelFact)knownAv.Key).Point.y)
                                                                        && ((OMF)((OMA)((RECARG)((OMA)((OMA)variable).arguments.ElementAt(0)).arguments.ElementAt(1)).value).arguments.ElementAt(2)).f.Equals(((CogwheelFact)knownAv.Key).Point.z));
                int numberOfElementsPerRow = AData.ElementAt(0).Count;
                //Create new row with zero-entries
                List<double> newADataRow = new List<double>(new double[numberOfElementsPerRow]);
                //Set 1 in AData for the column of the corresponding variable
                newADataRow[variables.IndexOf(knownAvVariable)] = 1;
                //Add new column to AData
                AData.Add(newADataRow);
                //Add known angularVelocity to bData
                bData.Add(knownAv.Value);
            }
        }
    }

    private static Dictionary<Fact, float> getNewlyDiscoveredAvsMap(Dictionary<Fact, float> knownAvMap, List<MMTTerm> variables, Vector<double> glsSolution) {
        Dictionary<Fact, float> discoveredAvsMap = new Dictionary<Fact, float>();

        //Find KeyValuePairs<Fact,float> ...
        if (glsSolution.Count.Equals(variables.Count))
        {
            foreach (Fact fact in GameState.Facts) {
                if (fact.GetType().Equals(typeof(CogwheelFact))) {
                    MMTTerm knownAvVariable = variables.Find(variable => variable.isSimplifiedCogwheelAvTerm()
                                                                        && ((OMF)((RECARG)((OMA)((OMA)variable).arguments.ElementAt(0)).arguments.ElementAt(0)).value).f.Equals(((CogwheelFact)fact).Radius)
                                                                        && ((OMF)((OMA)((RECARG)((OMA)((OMA)variable).arguments.ElementAt(0)).arguments.ElementAt(1)).value).arguments.ElementAt(0)).f.Equals(((CogwheelFact)fact).Point.x)
                                                                        && ((OMF)((OMA)((RECARG)((OMA)((OMA)variable).arguments.ElementAt(0)).arguments.ElementAt(1)).value).arguments.ElementAt(1)).f.Equals(((CogwheelFact)fact).Point.y)
                                                                        && ((OMF)((OMA)((RECARG)((OMA)((OMA)variable).arguments.ElementAt(0)).arguments.ElementAt(1)).value).arguments.ElementAt(2)).f.Equals(((CogwheelFact)fact).Point.z));
                    //If an element was found
                    if (knownAvVariable != null) {
                        double newlyDiscoveredAv = glsSolution.ElementAt(variables.IndexOf(knownAvVariable));
                        //Now we know that the Fact = fact should rotate with angularVelocity = newlyDiscoveredAv
                        discoveredAvsMap.Add(fact, (float)newlyDiscoveredAv);
                    }
                }
            }
        }
        else {
            Debug.Log("KnowledgeBasedSimulation.getNewlyDiscoveredAvsMap: Variables and GLS-Solution don't have the same number of elements.");
            return null;
        }

        //Build relative complement A \ B => discoveredAvsMap \ knownAvMap
        return discoveredAvsMap.Except(knownAvMap).ToDictionary(x => x.Key, x => x.Value);
    }
}