#!/usr/bin/env python3
import json, copy

scores_to_keep = set()
with open("scores_to_keep.txt") as f:
    for line in f:
        line = line.strip()
        if line:
            scores_to_keep.add(int(line))

with open("collections_with_scores.json") as f:
    collections = json.load(f)

target = None
for collection in collections:
    if collection["Name"] == "MOsu examples":
        target = collection
        break

if target is None:
    print("MOsu examples collection not found")
    exit(1)

filtered_scores = []
for score in target["Scores"]:
    if score["RulesetShortName"] != "mosususu":
        continue
    if score["TotalScore"] not in scores_to_keep:
        continue
    # Cap approach_rate at 10 in DA mod settings
    score = copy.deepcopy(score)
    for mod in score.get("Mods", []):
        settings = mod.get("settings", {})
        if "approach_rate" in settings and settings["approach_rate"] > 10:
            settings["approach_rate"] = 10
    filtered_scores.append(score)

result = [{
    "Name": target["Name"],
    "BeatmapMD5Hashes": target["BeatmapMD5Hashes"],
    "Scores": filtered_scores
}]

with open("example_collections.json", "w") as f:
    json.dump(result, f, indent=2)

print(f"Wrote {len(result)} collections with {sum(len(c['Scores']) for c in result)} scores")
