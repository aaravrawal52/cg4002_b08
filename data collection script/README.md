# Steps to use data collection script 

## Step 1

Using the sensorcollection.py script, change the person name that you are recording the gesture for

## Step 2 

Change the gesture type that you are recording the gesture.
This will serve as the folder and csv paths.

## Step 3

Go to the source code for hardware and under the sketch_nov_5.ino file search for Data Collection for ML
Uncomment the current section.

## Step 4 

Just above it, you can choose to change the "IDLE" tag to "UNCLASSIFIED" for easier search of how many gesture windows/samples have been collected in the csv later.

## Step 5 

Reupload the code on to the firebeetle, ensure that comms is not enabled between the firebeetle and the relay laptop

