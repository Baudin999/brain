---
kind: information
subjects: []
named-by: user
born: 2026-11-02
session: 9180db8c-e762-4a19-80e4-c2f0295db02f
---
# Home shows the constellation: every graph as one sky of clusters, so we learn by seeing the nodes

Home is the first page in brain's window. It shows every node and every edge of personal, global and every project as one sky. Like a solar system or a galaxy, the nodes form clusters, some larger, some smaller, each in its project's color. Personal is the sun at the center, global the ring around it, and the projects spread around them: the larger clusters first, the smaller ones between them, placed so the edges between projects cross the middle as little as possible. Every node belongs to a cluster, also a node without an edge. A large cluster reads as a galaxy: a bright core of its well-connected nodes and a sparse halo of the loosely connected ones, whose stars fade in as the user zooms. On the dark theme the sky lies on one still backdrop image of a few faint stars and soft painted nebulae, for the feeling of space; it is decoration tied to no cluster, the image is a file the user can swap for their own, and a setting turns it off.

Visual cues tell the nodes apart: information, an idea, a node with only a title and a node without an edge each look different, and a node grows with its edges. An edge's thickness is its weight. Edges are quiet and gray beneath the nodes, and the cluster names lie above everything, with their node count, like `global (40)`. A title never covers a star: the selected node's title always shows, then the lit ones, then the most connected, more as the user zooms.

The sky is beautiful, and its beauty moves in light, never in place. Each cluster lies on a faint glow in its color, global's a band that starts at the ring's inner edge, so the core and its ring read before their stars do. The well-connected stars wear a still halo. Light also carries meaning: a spark runs along an edge travelled today, a node born today shines, and a faded edge dims like dying light. The view glides instead of jumping, and the cluster names are set like a star atlas. Every effect has its own setting, and the Motion setting stops everything that moves. Beauty never costs speed: the sky is painted once and held still, a pan, zoom or glide moves the finished picture and paints it sharp once the view settles, and the only thing that repaints on its own is a running spark, in its own small box.

The sky settles and holds; nodes cannot be dragged. A node takes its place in the order its file entered git, beside the neighbours it entered with, so neither a new node nor a later link ever moves a node already there. Personal stays the core and global a ring wholly outside it: as personal grows, the ring is pushed outward whole, each global node keeping its angle, and a global node never moves a personal one. Only where a whole project sits, and how far out the ring lies, may shift as the graphs grow. Scroll zooms, a drag pans, Alt-R resets the view. Hovering a node shows a panel that reads like the node without opening it, and holding Shift makes it sticky so it can be scrolled. One click selects a node and lights it, its edges and its neighbours while the rest dims. A second click opens it in the middle pane, and Esc closes it. Typing after `/` lights the found nodes the same way, and they grow a little. Looking at the sky never travels an edge.

## Why
The user wants to learn by seeing the nodes: which clusters are large, which nodes stand alone, where the bridges run. Obsidian's galaxy view is the inspiration, and the backdrop gives it a hopeful, Star Trek feeling of space. The user wants Home more beautiful and wanted every effect, each switchable because this machine's CPU is fragile and they will not always want them all on. The core and its ring were lost twice, once when the sky learned to hold still and once under per-cluster nebulae that blurred the band, and the user wanted them back: "it felt amazing". The moving far stars and the nebulae gave too many problems, so a still image took their place. Home must render on a potato: the sky once drove the GPU to 50-80% while panning, beside vLLM, so the halos stopped breathing and the sky stopped repainting while it moves. Dragging a node once the sky has settled has no value, and a sky that holds still is one the user can learn their way around. Visual cues matter, so every kind of node needs its own look.

## Edges
- follows -> personal:three-tiers
- uses -> project-colors
- respects -> how-reading-works
