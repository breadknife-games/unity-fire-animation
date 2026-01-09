using System.Collections.Generic;
using System.Linq;
using PDNWrapper;
using PhotoshopFile;

namespace FireAnimation
{
    public class PsdParser
    {
        internal List<GameObjectGroup> ParsePsd(PsdFile file, string psdFileName)
        {
            var rootNodes = BuildLayerTree(file.Layers);
            var animationsByColor = new Dictionary<LayerColor, List<SpriteAnimation>>();

            foreach (var rootNode in rootNodes)
                FindAnimations(rootNode, animationsByColor, psdFileName);

            // Convert to GameObjectGroups, filtering out invalid colors
            var groups = new List<GameObjectGroup>();
            foreach (var kvp in animationsByColor)
            {
                if (!kvp.Key.IsValidGroupColor())
                    continue;

                if (kvp.Value.Count == 0)
                    continue;

                var group = new GameObjectGroup
                {
                    Name = kvp.Value[0].Name,
                    Color = kvp.Key,
                    Animations = kvp.Value
                };
                groups.Add(group);
            }

            return groups;
        }

        private void FindAnimations(
            LayerNode node,
            Dictionary<LayerColor, List<SpriteAnimation>> animationsByColor,
            string psdFileName)
        {
            if (!node.IsGroup)
                return;

            // Check if this node is an animation (has texture children)
            var hasTextureChildren = node.Children.Any(child =>
                child.IsGroup && GetTextureType(child.Color) != TextureType.Unknown);

            if (hasTextureChildren)
            {
                var animation = ProcessAnimationNode(node, psdFileName);
                if (animation != null && animation.Textures.Count > 0)
                {
                    var groupColor = node.Color;
                    if (!animationsByColor.TryGetValue(groupColor, out var list))
                    {
                        list = new List<SpriteAnimation>();
                        animationsByColor[groupColor] = list;
                    }

                    list.Add(animation);
                }

                return;
            }

            foreach (var child in node.Children)
                FindAnimations(child, animationsByColor, psdFileName);
        }

        private SpriteAnimation ProcessAnimationNode(LayerNode node, string psdFileName)
        {
            var animationName = BuildAnimationName(psdFileName, node);
            var texturesByType = new Dictionary<TextureType, AnimationTexture>();

            foreach (var child in node.Children)
            {
                if (!child.IsGroup)
                    continue;

                var textureType = GetTextureType(child.Color);
                if (textureType == TextureType.Unknown)
                    continue;

                // Get or create the texture for this type
                if (!texturesByType.TryGetValue(textureType, out var texture))
                {
                    texture = new AnimationTexture
                    {
                        Type = textureType,
                        Frames = new List<AnimationFrame>()
                    };
                    texturesByType[textureType] = texture;
                }

                // Process this node's frames and merge into the texture
                ProcessTextureNodeGroup(texture, child);
            }

            var animation = new SpriteAnimation
            {
                Name = animationName,
                Textures = texturesByType.Values.ToList()
            };

            return animation;
        }

        private string BuildAnimationName(string psdFileName, LayerNode node)
        {
            var pathParts = new List<string> { psdFileName };
            var path = new List<string>();
            var current = node;
            while (current != null)
            {
                path.Insert(0, current.Name);
                current = current.Parent;
            }

            pathParts.AddRange(path);
            return string.Join("_", pathParts);
        }

        internal void ResolveLayersFromDocument(List<GameObjectGroup> groups, Document document)
        {
            var layerLookup = new Dictionary<int, BitmapLayer>();
            BuildLayerLookup(document.Layers, layerLookup);

            foreach (var group in groups)
            {
                foreach (var animation in group.Animations)
                {
                    foreach (var texture in animation.Textures)
                    {
                        foreach (var frame in texture.Frames)
                        {
                            frame.BitmapLayers = new List<BitmapLayer>();
                            foreach (var layerId in frame.LayerIDs)
                            {
                                if (layerLookup.TryGetValue(layerId, out var bitmapLayer))
                                {
                                    frame.BitmapLayers.Add(bitmapLayer);
                                }
                            }
                        }
                    }
                }
            }
        }

        private void BuildLayerLookup(IEnumerable<BitmapLayer> layers, Dictionary<int, BitmapLayer> lookup)
        {
            foreach (var layer in layers)
            {
                lookup[layer.LayerID] = layer;
                if (layer.ChildLayer != null)
                {
                    BuildLayerLookup(layer.ChildLayer, lookup);
                }
            }
        }

        private void ProcessTextureNodeGroup(AnimationTexture texture, LayerNode node)
        {
            if (node.Color == LayerColor.Red)
                return;

            if (!node.IsGroup)
                return;

            if (!IsFrameGroup(node))
            {
                foreach (var child in node.Children)
                    ProcessTextureNodeGroup(texture, child);
                return;
            }

            while (texture.Frames.Count < node.Children.Count)
            {
                texture.Frames.Add(new AnimationFrame
                {
                    LayerIDs = new List<int>()
                });
            }

            for (var i = 0; i < node.Children.Count; i++)
            {
                var reversedIndex = node.Children.Count - 1 - i;
                texture.Frames[i].LayerIDs.Add(node.Children[reversedIndex].LayerId);
            }
        }

        private bool IsFrameGroup(LayerNode node)
        {
            if (!node.IsGroup)
                return false;

            return node.Children.All(child => !child.IsGroup);
        }

        private TextureType GetTextureType(LayerColor color)
        {
            return color switch
            {
                LayerColor.Gray => TextureType.Albedo,
                LayerColor.Indigo => TextureType.LightingRegion,
                _ => TextureType.Unknown
            };
        }

        private List<LayerNode> BuildLayerTree(List<PhotoshopFile.Layer> layers)
        {
            var rootNodes = new List<LayerNode>();
            var groupStack = new Stack<LayerNode>();

            for (var i = layers.Count - 1; i >= 0; i--)
            {
                var layer = layers[i];
                var sectionType = GetLayerSectionType(layer);

                if (sectionType == LayerSectionType.SectionDivider)
                {
                    if (groupStack.Count > 0)
                        groupStack.Pop();
                }
                else if (sectionType == LayerSectionType.OpenFolder ||
                         sectionType == LayerSectionType.ClosedFolder)
                {
                    var groupNode = new LayerNode
                    {
                        Name = layer.Name,
                        LayerId = layer.LayerID,
                        IsGroup = true,
                        Color = layer.GetColor(),
                        Children = new List<LayerNode>(),
                        Parent = groupStack.Count > 0 ? groupStack.Peek() : null
                    };

                    if (groupStack.Count > 0)
                        groupStack.Peek().Children.Add(groupNode);
                    else
                        rootNodes.Add(groupNode);

                    groupStack.Push(groupNode);
                }
                else
                {
                    var layerNode = new LayerNode
                    {
                        Name = layer.Name,
                        LayerId = layer.LayerID,
                        IsGroup = false,
                        Color = layer.GetColor(),
                        Children = new List<LayerNode>(),
                        Parent = groupStack.Count > 0 ? groupStack.Peek() : null
                    };

                    if (groupStack.Count > 0)
                        groupStack.Peek().Children.Add(layerNode);
                    else
                        rootNodes.Add(layerNode);
                }
            }

            return rootNodes;
        }

        private LayerSectionType GetLayerSectionType(PhotoshopFile.Layer layer)
        {
            var sectionInfo = layer.AdditionalInfo
                .OfType<LayerSectionInfo>()
                .FirstOrDefault();

            return sectionInfo?.SectionType ?? LayerSectionType.Layer;
        }

        private class LayerNode
        {
            public string Name { get; set; }
            public int LayerId { get; set; }
            public bool IsGroup { get; set; }
            public LayerColor Color { get; set; }
            public List<LayerNode> Children { get; set; }
            public LayerNode Parent { get; set; }
        }
    }
}
