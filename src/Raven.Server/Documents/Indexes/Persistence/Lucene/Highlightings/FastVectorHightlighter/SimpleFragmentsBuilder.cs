/*
 * Licensed to the Apache Software Foundation (ASF) under one or more
 * contributor license agreements.  See the NOTICE file distributed with
 * this work for additional information regarding copyright ownership.
 * The ASF licenses this file to You under the Apache License, Version 2.0
 * (the "License"); you may not use this file except in compliance with
 * the License.  You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Collections.Generic;
using WeightedFragInfo = Lucene.Net.Search.Vectorhighlight.FieldFragList.WeightedFragInfo;

namespace Lucene.Net.Search.Vectorhighlight
{
   
    /// <summary>
    /// A simple implementation of FragmentsBuilder.
    /// </summary>
    public sealed class SimpleFragmentsBuilder : BaseFragmentsBuilder
    {
        /// <summary>
        /// a constructor.
        /// </summary>
        public SimpleFragmentsBuilder() : base()
        {
        }
                

        /// <summary>
        /// a constructor.
        /// </summary>
        /// <param name="preTags">array of pre-tags for markup terms</param>
        /// <param name="postTags">array of post-tags for markup terms</param>
        public SimpleFragmentsBuilder(String[] preTags, String[] postTags)
            : base(preTags, postTags)
        {

        }

        /// <summary>
        /// do nothing. return the source list.
        /// </summary>
        public override List<WeightedFragInfo> GetWeightedFragInfoList(List<WeightedFragInfo> src)
        {
            return src;
        }
    }
}
