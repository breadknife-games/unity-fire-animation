using System.Collections.Generic;
using System.Linq;
using PDNWrapper;
using PhotoshopFile;

namespace FireAnimation
{
    public class PsdParser
    {
        internal List<SpriteAnimation> ParsePsd(PsdFile file, string psdFileName)
        {
            var animations = new List<SpriteAnimation>();
            var rootNodes = BuildLayerTree(file.Layers);
            foreach (var rootNode in rootNodes)
                FindSpriteAnimations(rootNode, animations, psdFileName);

            return animations;
        }

        private void FindSpriteAnimations(
            LayerNode node,
            List<SpriteAnimation> animations,
            string psdFileName)
        {
            if (!node.IsGroup)
                return;

            var isSprite = node.Children.Any(child =>
                child.IsGroup && GetTextureType(child.Color) != TextureType.Unknown);

            if (isSprite)
            {
                var animationName = BuildAnimationName(psdFileName, node);
                var animation = new SpriteAnimation
                {
                    Name = animationName,
                    Textures = new List<AnimationTexture>()
                };

                foreach (var child in node.Children)
                {
                    if (child.IsGroup && GetTextureType(child.Color) != TextureType.Unknown)
                    {
                        var texture = ProcessTextureNode(child);
                        if (texture != null)
                            animation.Textures.Add(texture);
                    }
                }

                if (animation.Textures.Count > 0)
                    animations.Add(animation);

                return;
            }

            foreach (var child in node.Children)
                FindSpriteAnimations(child, animations, psdFileName);
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


        internal void ResolveLayersFromDocument(List<SpriteAnimation> animations, Document document)
        {
            var layerLookup = new Dictionary<int, BitmapLayer>();
            BuildLayerLookup(document.Layers, layerLookup);

            foreach (var animation in animations)
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

        private AnimationTexture ProcessTextureNode(LayerNode node)
        {
            if (node.Color == LayerColor.Red)
                return null;

            var textureType = GetTextureType(node.Color);
            if (textureType == TextureType.Unknown) return null;

            var texture = new AnimationTexture
            {
                Name = node.Name,
                Type = textureType,
                Frames = new List<AnimationFrame>()
            };

            ProcessTextureNodeGroup(texture, node);

            return texture;
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
                int reversedIndex = node.Children.Count - 1 - i;
                texture.Frames[i].LayerIDs.Add(node.Children[reversedIndex].LayerID);
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
                LayerColor.Seafoam => TextureType.Albedo,
                LayerColor.Fuchsia => TextureType.Normal,
                _ => TextureType.Unknown
            };
        }

        private List<LayerNode> BuildLayerTree(List<PhotoshopFile.Layer> layers)
        {
            var rootNodes = new List<LayerNode>();
            var groupStack = new Stack<LayerNode>();

            for (int i = layers.Count - 1; i >= 0; i--)
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
                        LayerID = layer.LayerID,
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
                        LayerID = layer.LayerID,
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
            public int LayerID { get; set; }
            public bool IsGroup { get; set; }
            public LayerColor Color { get; set; }
            public List<LayerNode> Children { get; set; }
            public LayerNode Parent { get; set; }
        }
    }
}
