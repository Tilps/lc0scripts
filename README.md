# lc0scripts

A bunch of small projects I've used while working on lc0.

Main one is policy rater - each of its 4 primary tasks that it supports requires a custom build of lc0 (Value score and book make use the same one, others are separate).
Each of these are in a branch of my fork of lc0 and will need updating any time backwards incompatible changes are introduced to the network file format.
I've included lc0 windows binaries built with specific version of cuda/cudnn - but they can be replaced by a fresh compile from the corresponding branch.
I've also included point in time backups of the accumulation files in case it saves some backfill time if someone takes over running this from me.
The primary program.cs of policy rater has some fill in the blank spots since your syzygy won't be installed where mine is, nor will your github containing the gists you want to upload to.
Rater core makes assumptions that Git is installed in a default install location - you may want to hand edit that too.

matchtobook is a tool for creating a separate opening for every position in every game in an input pgn.  Tested with tcec tournament pgn files.
lotsofeval is a very basic tool I used to create the evals.txt that value scorer uses as its baseline.

Note that I've included some large text files as zips since otherwise they would be over the 100MB github file size limit.  They need to be unpacked before use.


 
